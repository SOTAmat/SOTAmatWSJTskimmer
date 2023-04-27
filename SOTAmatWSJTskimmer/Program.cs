using M0LTE.WsjtxUdpLib.Client;
using M0LTE.WsjtxUdpLib.Messages;
using M0LTE.WsjtxUdpLib.Messages.Out;
using System.Net;
using System.Text.RegularExpressions;

namespace SOTAmatWSJTskimmer
{
    class Program
    {


        static int Main(string[] args)
        {
            bool connected = false;

            ParseArgs.PrintVersion();

            // Parse the args passed in and set the variables
            Configuration config = ParseArgs.Parse(args);

            // Validate that we have reasonable instructions and that the user credentials are good
            if (config.ValidParse == false || SOTAmatClient.Authenticate(config).Result != true)
            {
                Console.WriteLine();
                Console.WriteLine("Enter a key to exit...");
                Console.ReadKey();
                return 2;
            }

            // Great.  With the bookeeping out of the way, let's connect to WSJT-X and start listening
            try
            {

                Console.WriteLine("[type CTRL-C to exit]\n");
                Console.WriteLine($"Attempting to connect {config.Callsign} to WSJT-X via {(config.Multicast ? "multicast" : "direct")} UDP at address {config.Address}...\n");

                // Set a recurring 5 second timer to check if we haven't received a heartbeat from WSJT-X in the last 30 seconds.
                // If heartbeat has been lost, let the user know so they can fix it!
                Timer timer = new((e) =>
                {
                    if (connected && (DateTime.Now - config.LastHeartbeat).TotalSeconds > 30)
                    {
                        Console.WriteLine("ERROR: No heartbeat received from WSJT-X in over 30 seconds.  Is WSJT-X running?  Connected?");
                        connected = false;
                    }
                }, null, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(5));

                using var client = new WsjtxClient((msg, from) =>
                {
                    if (msg is StatusMessage statusMsg)
                    {
                        if (!connected)
                        {
                            connected = true;
                            Console.WriteLine("Connected to WSJT-X!  Listening for SOTAmat messages...\n");
                        }

                        // Store the current time so that we can measure elapsed time from this point
                        config.LastHeartbeat = DateTime.Now;

                        // Grab the dial frequency
                        config.DialFrequency = (long)statusMsg.DialFrequency;
                        // Grab the mode
                        config.Mode = statusMsg.Mode;
                    }

                    if (connected && msg is HeartbeatMessage)
                    {
                        // Store the current time so that we can measure elapsed time from this point
                        config.LastHeartbeat = DateTime.Now;
                    }

                    if (connected && msg is DecodeMessage decodedMsg)
                    {
                        // Store the current time so that we can measure elapsed time from this point
                        config.LastHeartbeat = DateTime.Now;

                        if (config.Debug) Console.WriteLine($"Message decoded: {decodedMsg.Message}");

                        // If the statusMsg is a potential SOTAmat statusMsg, send it to the SOTAmat server
                        string pattern = @"^(S(T(M(T)?)?|OTAM(T|AT)?)?M?)\s([0-9A-Z]{1,2}[0-9][0-9A-Z]{1,3})(/[0-9A-Z]{1,4})+$";
                        Regex regex = new(pattern, RegexOptions.IgnoreCase);

                        if (decodedMsg.Message.Length == 13 && regex.IsMatch(decodedMsg.Message))
                        {
                            Console.WriteLine($"SOTAmat Message Received!  Sending to SOTAmat Server: {decodedMsg.Message}");
                            // Send the statusMsg to the SOTAmat server
                            Task.Run(() => SOTAmatClient.Send(config, decodedMsg));
                        }
                    }
                }, ipAddress: IPAddress.Parse(config.Address), port: config.Port, multicast: config.Multicast, debug: config.Logging);

                Thread.CurrentThread.Join();

                return 0;
            }
            catch (System.Net.Sockets.SocketException socketEx)
            {
                if (config.Multicast)
                {
                    Console.WriteLine();
                    Console.WriteLine("NETWORK ERROR: unknown failure connecting to Multicast network port.");
                    Console.WriteLine(socketEx.Message);
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("NETWORK ERROR: failed to connect to unicast port.  Only one WSJT client can connect at a time, or configure WSJT-X and SOTAmatWSJTskimmer for Multicast.");
                    Console.WriteLine(socketEx.Message);
                }
                return 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("UNKNOWN ERROR: Internal SOTAmatWSJTskimmer error.  Please report to support@sotamat.com");
                Console.WriteLine(ex.Message);
                Console.WriteLine();
                Console.WriteLine("Enter a key to exit...");
                Console.ReadKey();
                return 1;
            }
        }

    }
}
