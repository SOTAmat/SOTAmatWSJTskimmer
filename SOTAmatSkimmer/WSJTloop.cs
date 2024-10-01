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
        bool connected = false;
        private WsjtxClient? client;
        private DateTime lastConnectionAttempt = DateTime.MinValue;
        private const int RECONNECT_INTERVAL_SECONDS = 15;
        private SOTAmatClient smClient;

        public WsjtxLooper(Configuration config)
        {
            Config = config;
            connected = false;
            smClient = new SOTAmatClient();
        }

        public int Loop()
        {
            Timer heartbeatTimer = new((e) =>
            {
                if (connected && (DateTime.Now - Config.LastHeartbeat).TotalSeconds > 30)
                {
                    ConsoleHelper.SafeWriteLine($"ERROR: No heartbeat received from WSJT-X in over 30 seconds. Is WSJT-X running? Connected?\n", true, ConsoleColor.Red);
                    connected = false;
                }
            }, null, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(10));

            Timer reconnectTimer = new((e) =>
            {
                if (!connected && (DateTime.Now - lastConnectionAttempt).TotalSeconds >= RECONNECT_INTERVAL_SECONDS)
                {
                    AttemptConnection();
                }
            }, null, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1));

            try
            {
                AttemptConnection();

                // Keep the application running
                while (true)
                {
                    Thread.Sleep(1000);
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

        private void AttemptConnection()
        {
            lastConnectionAttempt = DateTime.Now;
            try
            {
                client = new WsjtxClient((msg, from) =>
                {
                    if (msg is StatusMessage statusMsg)
                    {
                        if (!connected)
                        {
                            connected = true;
                            ConsoleHelper.SafeWriteLine($"Connected to WSJT-X! Listening for SOTAmat messages...\n", true, ConsoleColor.Green);
                        }

                        Config.LastHeartbeat = DateTime.Now;
                        Config.DialFrequency = (long)statusMsg.DialFrequency;
                        Config.Mode = statusMsg.Mode;
                    }

                    if (connected && msg is HeartbeatMessage)
                    {
                        Config.LastHeartbeat = DateTime.Now;
                    }

                    if (connected && msg is DecodeMessage decodedMsg)
                    {
                        Config.LastHeartbeat = DateTime.Now;
                        smClient.ParseAndExecuteMessage(Config,
                                                        snr: decodedMsg.Snr,
                                                        deltaTime: decodedMsg.DeltaTime,
                                                        message: decodedMsg.Message,
                                                        deltaFrequency: decodedMsg.DeltaFrequency);
                    }
                }, ipAddress: IPAddress.Parse(Config.Address), port: Config.Port, multicast: Config.Multicast, debug: Config.Logging);
            }
            catch (System.Net.Sockets.SocketException)
            {
                connected = false;
                ConsoleHelper.SafeWriteLine($"NETWORK ERROR: Failed to connect to WSJT-X. Is it running?", true, ConsoleColor.Red);
                if (Config.Multicast)
                {
                    ConsoleHelper.SafeWriteLine("Unknown failure connecting to Multicast network port.", false, ConsoleColor.Yellow);
                }
                else
                {
                    ConsoleHelper.SafeWriteLine("Failed to connect to unicast port. Only one WSJT client can connect at a time, or configure WSJT-X and SOTAmatSkimmer for Multicast.", false, ConsoleColor.Yellow);
                }
                ConsoleHelper.SafeWriteLine($"Will attempt to reconnect in {RECONNECT_INTERVAL_SECONDS} seconds.", false, ConsoleColor.Yellow);
            }
            catch (Exception ex)
            {
                connected = false;
                ConsoleHelper.SafeWriteLine($"GENERAL ERROR: {ex.Message}", true, ConsoleColor.Red);
            }
        }
    }
}