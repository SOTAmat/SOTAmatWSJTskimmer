using M0LTE.WsjtxUdpLib.Client;
using M0LTE.WsjtxUdpLib.Messages;
using M0LTE.WsjtxUdpLib.Messages.Out;
using System.Net;
using SOTAmatSkimmer.Utilities;

namespace SOTAmatSkimmer
{
    public class WsjtxLooper
    {
        Configuration Config { get; set; }
        private bool connected = false;
        private WsjtxClient? client;
        private CancellationTokenSource? cts;
        private Task? clientTask;
        private Timer? heartbeatTimer;
        private readonly object stateLock = new();
        private int disconnectInProgress = 0;

        public WsjtxLooper(Configuration config)
        {
            Config = config;
            connected = false;
            WorkerHealthState.SetConnected(false);
        }

        public int Loop()
        {
            try
            {
                heartbeatTimer = new(CheckHeartbeat, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));

                while (true)
                {
                    Console.WriteLine($"Connecting {Config.Callsign} to {(Config.SparkSDRmode ? "SparkSDR" : "WSJT-X")} via {(Config.Multicast ? "multicast" : "direct")} {(Config.SparkSDRmode ? "websocket" : "UDP")} at {Config.Address} with grid {Config.Gridsquare}:\n");
                    Console.WriteLine();

                    ConnectAndLoop();
                    int reconnectSeconds = Math.Max(1, Config.ReconnectIntervalSeconds);
                    ConsoleHelper.SafeWriteLine($"Connection cycle ended. Pausing for {reconnectSeconds} seconds before next attempt...", false, ConsoleColor.DarkGray);
                    Thread.Sleep(TimeSpan.FromSeconds(reconnectSeconds));
                }
            }
            catch (Exception ex)
            {
                WorkerHealthState.RecordError();
                ConsoleHelper.SafeWriteLine("UNKNOWN ERROR: Internal SOTAmatSkimmer error. Please report to support@sotamat.com", true, ConsoleColor.Red);
                ConsoleHelper.SafeWriteLine(ex.Message, false);
                ConsoleHelper.SafeWriteLine("Press any key to exit...", false, ConsoleColor.Yellow);
                Console.ReadKey();
                return 1;
            }
            finally
            {
                heartbeatTimer?.Dispose();
                heartbeatTimer = null;
            }
        }

        private void ConnectAndLoop()
        {
            ConsoleHelper.SafeWriteLine("Initiating new connection sequence in ConnectAndLoop...", false, ConsoleColor.Cyan);
            CancellationTokenSource localCts = new();
            Task localTask = Task.CompletedTask;

            try
            {
                lock (stateLock)
                {
                    cts = localCts;
                    clientTask = null;
                    client = null;
                }

                ConsoleHelper.SafeWriteLine("Starting WsjtxClient task...", false, ConsoleColor.Gray);
                localTask = Task.Run(() => RunClient(localCts.Token), CancellationToken.None);

                lock (stateLock)
                {
                    clientTask = localTask;
                }

                localTask.Wait();
            }
            catch (AggregateException ae)
            {
                WorkerHealthState.RecordError();
                HandleConnectionFailure($"GENERAL ERROR during ConnectAndLoop: {ae.Flatten().InnerException?.Message ?? ae.Message}");
            }
            catch (Exception ex)
            {
                WorkerHealthState.RecordError();
                HandleConnectionFailure($"GENERAL ERROR during ConnectAndLoop: {ex.Message}");
            }
            finally
            {
                ConsoleHelper.SafeWriteLine("ConnectAndLoop finally block: Cleaning up CancellationTokenSource and client resources...", false, ConsoleColor.DarkGray);
                lock (stateLock)
                {
                    if (ReferenceEquals(cts, localCts))
                    {
                        cts = null;
                    }

                    if (ReferenceEquals(clientTask, localTask))
                    {
                        clientTask = null;
                    }

                    client = null;
                }

                localCts.Dispose();
                Interlocked.Exchange(ref disconnectInProgress, 0);
                ConsoleHelper.SafeWriteLine("ConnectAndLoop cleanup complete.", false, ConsoleColor.DarkGray);
            }
        }

        private void RunClient(CancellationToken token)
        {
            WsjtxClient? localClient = null;
            try
            {
                localClient = new WsjtxClient(OnWsjtxMessage, ipAddress: IPAddress.Parse(Config.Address), port: Config.Port, multicast: Config.Multicast, debug: Config.Logging);

                lock (stateLock)
                {
                    client = localClient;
                }

                token.WaitHandle.WaitOne();
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                WorkerHealthState.RecordError();
                HandleConnectionFailure($"NETWORK ERROR: Failed to connect to WSJT-X. Is it running? Details: {ex.Message}");
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    WorkerHealthState.RecordError();
                    HandleConnectionFailure($"GENERAL ERROR during WSJT client loop: {ex.Message}");
                }
            }
            finally
            {
                try
                {
                    localClient?.Dispose();
                }
                catch (Exception ex)
                {
                    WorkerHealthState.RecordError();
                    ConsoleHelper.SafeWriteLine($"WARNING: Error while disposing WSJT client: {ex.Message}", true, ConsoleColor.DarkYellow);
                }

                lock (stateLock)
                {
                    if (ReferenceEquals(client, localClient))
                    {
                        client = null;
                    }
                }

                connected = false;
                WorkerHealthState.SetConnected(false);
            }
        }

        private void OnWsjtxMessage(WsjtxMessage msg, IPEndPoint from)
        {
            try
            {
                Config.LastHeartbeat = DateTime.Now;
                WorkerHealthState.RecordSourceMessage();

                if (!connected)
                {
                    connected = true;
                    WorkerHealthState.SetConnected(true);
                    ConsoleHelper.SafeWriteLine("Connected to WSJT-X! Listening for SOTAMAT messages...\n", true, ConsoleColor.Green);
                }

                if (msg is StatusMessage statusMsg)
                {
                    Config.DialFrequency = (long)statusMsg.DialFrequency;
                    Config.Mode = statusMsg.Mode;
                }

                if (msg is DecodeMessage decodedMsg)
                {
                    SOTAmatClient.ParseAndExecuteMessage(Config,
                                                            snr: decodedMsg.Snr,
                                                            deltaTime: decodedMsg.DeltaTime,
                                                            message: decodedMsg.Message,
                                                            deltaFrequency: decodedMsg.DeltaFrequency);
                    WorkerHealthState.RecordDecodeHandled();
                }
            }
            catch (Exception ex)
            {
                WorkerHealthState.RecordError();
                ConsoleHelper.SafeWriteLine($"ERROR: Exception in WSJT callback: {ex.Message}", true, ConsoleColor.Red);
            }
        }

        private void HandleConnectionFailure(string errorMessage)
        {
            connected = false;
            WorkerHealthState.SetConnected(false);
            WorkerHealthState.RecordError();
            ConsoleHelper.SafeWriteLine(errorMessage, true, ConsoleColor.Red);
            if (Config.Multicast)
            {
                ConsoleHelper.SafeWriteLine("Unknown failure connecting to Multicast network port.", false, ConsoleColor.Yellow);
            }
            else
            {
                ConsoleHelper.SafeWriteLine("Failed to connect to unicast port. Only one WSJT client can connect at a time, or configure WSJT-X and SOTAmatSkimmer for Multicast.", false, ConsoleColor.Yellow);
            }

            int reconnectSeconds = Math.Max(1, Config.ReconnectIntervalSeconds);
            ConsoleHelper.SafeWriteLine($"Attempting reconnect in {reconnectSeconds} sec.", false, ConsoleColor.Yellow);
        }

        private void CheckHeartbeat(object? state)
        {
            if (connected && (DateTime.Now - Config.LastHeartbeat).TotalSeconds > Config.HeartbeatTimeoutSeconds)
            {
                ConsoleHelper.SafeWriteLine($"ERROR: No heartbeat from WSJT-X in {Config.HeartbeatTimeoutSeconds} sec. Connection lost.", true, ConsoleColor.Red);
                HandleDisconnection();
            }
        }

        private void HandleDisconnection()
        {
            if (Interlocked.Exchange(ref disconnectInProgress, 1) == 1)
            {
                return;
            }

            connected = false;
            WorkerHealthState.SetConnected(false);
            ConsoleHelper.SafeWriteLine("ERROR: Connection to WSJT-X lost or timed out. Initiating cleanup...", true, ConsoleColor.Red);

            CancellationTokenSource? ctsSnapshot;
            Task? taskSnapshot;
            lock (stateLock)
            {
                ctsSnapshot = cts;
                taskSnapshot = clientTask;
            }

            if (ctsSnapshot == null && taskSnapshot == null)
            {
                ConsoleHelper.SafeWriteLine("WARNING: No active WSJT client state during HandleDisconnection.", false, ConsoleColor.DarkYellow);
                Interlocked.Exchange(ref disconnectInProgress, 0);
                return;
            }

            if (ctsSnapshot != null && !ctsSnapshot.IsCancellationRequested)
            {
                ConsoleHelper.SafeWriteLine("Attempting to cancel client task...", false, ConsoleColor.Yellow);
                ctsSnapshot.Cancel();
            }

            if (taskSnapshot != null)
            {
                ConsoleHelper.SafeWriteLine("Waiting for client task to complete (max 5 seconds)...", false, ConsoleColor.Yellow);
                bool taskCompleted = false;
                try
                {
                    taskCompleted = taskSnapshot.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException ae)
                {
                    taskCompleted = true;
                    WorkerHealthState.RecordError();
                    ConsoleHelper.SafeWriteLine($"Client task ended with exception during disconnect: {ae.Flatten().InnerException?.Message ?? ae.Message}", false, ConsoleColor.DarkYellow);
                }
                catch (Exception ex)
                {
                    WorkerHealthState.RecordError();
                    ConsoleHelper.SafeWriteLine($"Unexpected error while waiting for client task: {ex.Message}", true, ConsoleColor.Red);
                }

                if (!taskCompleted)
                {
                    ConsoleHelper.SafeWriteLine("WARNING: Client task did not complete within timeout. Recovery will continue via main reconnect loop.", true, ConsoleColor.DarkYellow);
                    if (Config.WorkerMode)
                    {
                        WorkerHealthState.RecordError();
                        ConsoleHelper.SafeWriteLine("ERROR: WSJT cleanup exceeded timeout in worker mode. Exiting worker so supervisor can restart it.", true, ConsoleColor.Red);
                        Environment.Exit(1);
                    }
                }
            }

            int reconnectSeconds = Math.Max(1, Config.ReconnectIntervalSeconds);
            ConsoleHelper.SafeWriteLine($"Cleanup complete. Main loop will attempt reconnect in {reconnectSeconds} sec.", true, ConsoleColor.Yellow);
        }
    }
}
