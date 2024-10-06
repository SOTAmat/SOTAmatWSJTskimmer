using M0LTE.WsjtxUdpLib.Client;
using M0LTE.WsjtxUdpLib.Messages;
using M0LTE.WsjtxUdpLib.Messages.Out;
using System.Net;
using SOTAmatSkimmer.Utilities;
using System.Threading;
using System.Threading.Tasks;

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
            cts = new CancellationTokenSource();

            try
            {
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
            }
            catch (OperationCanceledException)
            {
                // This is expected when cancellation is requested
            }
            catch (System.Net.Sockets.SocketException)
            {
                HandleConnectionFailure("NETWORK ERROR: Failed to connect to WSJT-X. Is it running?");
            }
            catch (Exception ex)
            {
                HandleConnectionFailure($"GENERAL ERROR: {ex.Message}");
            }
            finally
            {
                cts.Dispose();
                cts = null;
                client = null;
                clientTask = null;
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
            cts?.Cancel();
            try
            {
                clientTask?.Wait();
            }
            catch (AggregateException)
            {
                // This is expected when task is canceled
            }
            ConsoleHelper.SafeWriteLine($"Disconnected from WSJT-X. Attempting reconnect in {RECONNECT_INTERVAL_SECONDS} sec.", true, ConsoleColor.Yellow);
        }

    }
}