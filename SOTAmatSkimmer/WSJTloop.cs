using M0LTE.WsjtxUdpLib.Client;
using M0LTE.WsjtxUdpLib.Messages.Out;
using System.Net;
using SOTAmatSkimmer.Utilities;

namespace SOTAmatSkimmer
{
    public class WsjtxLooper
    {
        Configuration Config { get; set; }
        bool connected = false;
        private WsjtxClient? client;
        private DateTime lastConnectionAttempt = DateTime.MinValue;
        private const int RECONNECT_INTERVAL_SECONDS = 15;
        private CancellationTokenSource? cts;
        private Task? clientTask;

        public WsjtxLooper(Configuration config)
        {
            Config = config;
            connected = false;
        }

        public int Loop()
        {
            try
            {
                Timer heartbeatTimer = new(CheckHeartbeat, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));

                while (true)
                {
                    Console.WriteLine($"Connecting {Config.Callsign} to {(Config.SparkSDRmode ? "SparkSDR" : "WSJT-X")} via {(Config.Multicast ? "multicast" : "direct")} {(Config.SparkSDRmode ? "websocket" : "UDP")} at {Config.Address} with grid {Config.Gridsquare}:\n");
                    Console.WriteLine();

                    ConnectAndLoop();
                    // Log after ConnectAndLoop completes, before sleeping
                    ConsoleHelper.SafeWriteLine($"Connection cycle ended. Pausing for {RECONNECT_INTERVAL_SECONDS} seconds before next attempt...", false, ConsoleColor.DarkGray);
                    Thread.Sleep(RECONNECT_INTERVAL_SECONDS * 1000);
                }
            }
            catch (Exception ex)
            {
                ConsoleHelper.SafeWriteLine($"UNKNOWN ERROR: Internal SOTAmatSkimmer error. Please report to support@sotamat.com", true, ConsoleColor.Red);
                ConsoleHelper.SafeWriteLine(ex.Message, false);
                ConsoleHelper.SafeWriteLine("Press any key to exit...", false, ConsoleColor.Yellow);
                Console.ReadKey();
                return 1;
            }
        }

        private void ConnectAndLoop()
        {
            lastConnectionAttempt = DateTime.Now;
            ConsoleHelper.SafeWriteLine("Initiating new connection sequence in ConnectAndLoop...", false, ConsoleColor.Cyan);
            cts = new CancellationTokenSource();

            try
            {
                ConsoleHelper.SafeWriteLine("Starting WsjtxClient task...", false, ConsoleColor.Gray);
                clientTask = Task.Run(() =>
                {
                    client = new WsjtxClient((msg, from) =>
                    {
                        Config.LastHeartbeat = DateTime.Now;
                        if (!connected)
                        {
                            connected = true;
                            ConsoleHelper.SafeWriteLine($"Connected to WSJT-X! Listening for SOTAMAT messages...\n", true, ConsoleColor.Green);
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
                        }
                    }, ipAddress: IPAddress.Parse(Config.Address), port: Config.Port, multicast: Config.Multicast, debug: Config.Logging);

                    while (!cts.Token.IsCancellationRequested)
                    {
                        Thread.Sleep(1000);
                    }

                    client.Dispose();
                }, cts.Token);

                clientTask.Wait(cts.Token);
                // This line is reached if Wait completes without OperationCanceledException, meaning graceful shutdown via cancellation.
                ConsoleHelper.SafeWriteLine("WsjtxClient task completed gracefully after cancellation.", false, ConsoleColor.Green);
            }
            catch (OperationCanceledException)
            {
                // This is expected when cancellation is requested
                ConsoleHelper.SafeWriteLine("WsjtxClient task was cancelled as expected (OperationCanceledException caught).", false, ConsoleColor.Yellow);
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                HandleConnectionFailure($"NETWORK ERROR: Failed to connect to WSJT-X. Is it running? Details: {ex.Message}");
            }
            catch (Exception ex)
            {
                HandleConnectionFailure($"GENERAL ERROR during ConnectAndLoop: {ex.Message}");
            }
            finally
            {
                ConsoleHelper.SafeWriteLine("ConnectAndLoop finally block: Cleaning up CancellationTokenSource and client resources...", false, ConsoleColor.DarkGray);
                cts.Dispose();
                cts = null;
                client = null;
                clientTask = null;
                ConsoleHelper.SafeWriteLine("ConnectAndLoop cleanup complete.", false, ConsoleColor.DarkGray);
            }
        }

        private void HandleConnectionFailure(string errorMessage)
        {
            connected = false;
            ConsoleHelper.SafeWriteLine(errorMessage, true, ConsoleColor.Red);
            if (Config.Multicast)
            {
                ConsoleHelper.SafeWriteLine("Unknown failure connecting to Multicast network port.", false, ConsoleColor.Yellow);
            }
            else
            {
                ConsoleHelper.SafeWriteLine("Failed to connect to unicast port. Only one WSJT client can connect at a time, or configure WSJT-X and SOTAmatSkimmer for Multicast.", false, ConsoleColor.Yellow);
            }
            ConsoleHelper.SafeWriteLine($"Attempting reconnect in {RECONNECT_INTERVAL_SECONDS} sec.", false, ConsoleColor.Yellow);
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
            connected = false;
            // Inform that cleanup is starting before attempting to cancel or wait.
            ConsoleHelper.SafeWriteLine($"ERROR: Connection to WSJT-X lost or timed out. Initiating cleanup...", true, ConsoleColor.Red);

            if (cts != null && !cts.IsCancellationRequested)
            {
                ConsoleHelper.SafeWriteLine("Attempting to cancel client task...", false, ConsoleColor.Yellow);
                cts.Cancel();
            }
            else if (cts == null)
            {
                ConsoleHelper.SafeWriteLine("WARNING: CancellationTokenSource is null during HandleDisconnection.", false, ConsoleColor.DarkYellow);
            }
            // No need for an else if cts.IsCancellationRequested is already true, that's fine.

            if (clientTask != null)
            {
                ConsoleHelper.SafeWriteLine("Waiting for client task to complete (max 5 seconds)...", false, ConsoleColor.Yellow);
                bool taskCompletedGracefully = false;
                try
                {
                    // Wait for the client task to finish, but with a timeout.
                    // clientTask is supposed to call client.Dispose() and then complete.
                    taskCompletedGracefully = clientTask.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException ae)
                {
                    // OperationCanceledException is often wrapped in AggregateException when a task is awaited.
                    taskCompletedGracefully = true; // Task is considered completed/terminated if it threw.
                    bool isCancellation = ae.InnerExceptions.Any(e => e is OperationCanceledException);
                    if (isCancellation)
                    {
                        ConsoleHelper.SafeWriteLine("Client task cancelled as expected.", false, ConsoleColor.Green);
                    }
                    else
                    {
                        ConsoleHelper.SafeWriteLine($"Client task completed with unexpected exception: {ae.InnerException?.Message}", false, ConsoleColor.DarkYellow);
                    }
                }
                catch (OperationCanceledException)
                {
                    taskCompletedGracefully = true; // Task is considered completed/terminated.
                    ConsoleHelper.SafeWriteLine("Client task explicitly cancelled as expected.", false, ConsoleColor.Green);
                }
                catch (Exception ex) // Catch any other unexpected exceptions from Wait itself
                {
                    taskCompletedGracefully = false; // Unclear state, assume not graceful.
                    ConsoleHelper.SafeWriteLine($"Unexpected error while waiting for client task: {ex.Message}", true, ConsoleColor.Red);
                }

                if (!taskCompletedGracefully)
                {
                    ConsoleHelper.SafeWriteLine("WARNING: Client task did not complete gracefully within the 5-second timeout. The client's Dispose method might be stuck. Continuing with reconnection attempt.", true, ConsoleColor.DarkYellow);
                }
            }
            else
            {
                ConsoleHelper.SafeWriteLine("Client task is null during HandleDisconnection, no task to wait for.", false, ConsoleColor.Yellow);
            }

            // Nullify fields here. The finally block in ConnectAndLoop will also do this if it's reached,
            // but doing it here ensures they are cleared from the perspective of HandleDisconnection's thread
            // and before the next reconnection messages are logged.
            clientTask = null;
            client = null; // Client should have been disposed by clientTask. If clientTask hung, client might be orphaned.

            // This message is now more accurate as ConnectAndLoop's finally block and the main Loop() handles actual retry.
            ConsoleHelper.SafeWriteLine($"Cleanup complete. Main loop will attempt reconnect in {RECONNECT_INTERVAL_SECONDS} sec.", true, ConsoleColor.Yellow);
        }

    }
}