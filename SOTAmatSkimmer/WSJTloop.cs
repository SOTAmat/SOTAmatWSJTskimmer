using M0LTE.WsjtxUdpLib.Client;
using M0LTE.WsjtxUdpLib.Messages;
using M0LTE.WsjtxUdpLib.Messages.Out;
using System.Net;

namespace SOTAmatSkimmer
{
    public class WsjtxLooper
    {
        Configuration Config {get; set;}
        bool connected = false;

        // Set up the class constructor
        public WsjtxLooper(Configuration config)
        {
            Config = config;
            connected = false;
        }
        public int Loop()
        {
            // Set a recurring 10 second timer to check if we haven't received a heartbeat from WSJT-X in the last 30 seconds.
            // If heartbeat has been lost, let the user know so they can fix it!
            Timer timer = new((e) =>
            {
                if (connected && (DateTime.Now - Config.LastHeartbeat).TotalSeconds > 30)
                {
                    Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} ERROR: No heartbeat received from WSJT-X in over 30 seconds.  Is WSJT-X running?  Connected?");
                    connected = false;
                }
            }, null, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(10));

            try
            {

                using var client = new WsjtxClient((msg, from) =>
                {
                    if (msg is StatusMessage statusMsg)
                    {
                        if (!connected)
                        {
                            connected = true;
                            Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} Connected to WSJT-X!  Listening for SOTAmat messages...\n");
                        }

                        // Store the current time so that we can measure elapsed time from this point
                        Config.LastHeartbeat = DateTime.Now;

                        // Grab the dial frequency
                        Config.DialFrequency = (long)statusMsg.DialFrequency;
                        // Grab the mode
                        Config.Mode = statusMsg.Mode;
                    }

                    if (connected && msg is HeartbeatMessage)
                    {
                        // Store the current time so that we can measure elapsed time from this point
                        Config.LastHeartbeat = DateTime.Now;
                    }

                    if (connected && msg is DecodeMessage decodedMsg)
                    {
                        // Store the current time so that we can measure elapsed time from this point
                        Config.LastHeartbeat = DateTime.Now;
                        SOTAmatClient.ParseAndExecuteMessage(   Config,
                                                                snr: decodedMsg.Snr,
                                                                deltaTime: decodedMsg.DeltaTime,
                                                                message: decodedMsg.Message,
                                                                deltaFrequency: decodedMsg.DeltaFrequency);
                    }
                }, ipAddress: IPAddress.Parse(Config.Address), port: Config.Port, multicast: Config.Multicast, debug: Config.Logging);

                Thread.CurrentThread.Join();

                return 0;
            }
            catch (System.Net.Sockets.SocketException socketEx)
            {
                if (Config.Multicast)
                {
                    Console.WriteLine();
                    Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} NETWORK ERROR: unknown failure connecting to Multicast network port.");
                    Console.WriteLine(socketEx.Message);
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} NETWORK ERROR: failed to connect to unicast port.  Only one WSJT client can connect at a time, or configure WSJT-X and SOTAmatSkimmer for Multicast.");
                    Console.WriteLine(socketEx.Message);
                }
                return 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} UNKNOWN ERROR: Internal SOTAmatSkimmer error.  Please report to support@sotamat.com");
                Console.WriteLine(ex.Message);
                Console.WriteLine();
                Console.WriteLine("Enter a key to exit...");
                Console.ReadKey();
                return 1;
            }

        }
    }
}
