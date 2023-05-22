using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SOTAmatWSJTskimmer
{
    public class ParseArgs
    {
        public static Configuration Parse(string[] args)
        {
            Configuration config = new();

            // Parse the args passed in and set the variables
            foreach (string arg in args)
            {
                if (arg.StartsWith("-a=") || arg.StartsWith("--address="))
                {
                    string tempAddress = arg[(arg.IndexOf('=') + 1)..];
                    if (IsValidIPAddress(tempAddress) || IsValidDNSName(tempAddress))
                    {
                        config.Address = tempAddress;
                    }
                    else
                    {
                        Console.WriteLine("Invalid address: " + tempAddress);
                        PrintHelp();
                        return config;
                    }
                }
                else if (arg.StartsWith("-c=") || arg.StartsWith("--callsign="))
                {
                    // Note that the callsign may already be set from the environment variable.
                    // If so, it will be set in the Configuration constructor.
                    // Here the user can override the environment variable with a command line argument.
                    config.Callsign = arg[(arg.IndexOf('=') + 1)..];
                }
                else if (arg.StartsWith("-d") || arg.StartsWith("--debug"))
                {
                    config.Debug = true;
                }
                else if (arg.StartsWith("-g=") || arg.StartsWith("--gridsquare="))
                {
                    // Note that the gridsquare may already be set from the environment variable.
                    // If so, it will be set in the Configuration constructor.
                    // Here the user can override the environment variable with a command line argument.
                    config.Gridsquare = arg[(arg.IndexOf('=') + 1)..];
                }
                else if (arg.StartsWith("-h") || arg.StartsWith("--help"))
                {
                    PrintHelp();
                }
                else if (arg.StartsWith("-l") || arg.StartsWith("--log"))
                {
                    config.Logging = true;
                }
                else if (arg.StartsWith("-m") || arg.StartsWith("--multicast"))
                {
                    config.Multicast = true;
                }
                else if (arg.StartsWith("-p=") || arg.StartsWith("--password="))
                {
                    // Note that the password may already be set from the environment variable.
                    // If so, it will be set in the Configuration constructor.
                    // Here the user can override the environment variable with a command line argument.
                    config.Password = arg[(arg.IndexOf('=') + 1)..];
                }
                else if (arg.StartsWith("-port=") || arg.StartsWith("--port="))
                {
                    config.Port = int.Parse(arg[(arg.IndexOf('=') + 1)..]);
                }
                else if (arg.StartsWith("-v") || arg.StartsWith("--version"))
                {
                    PrintVersion();
                }
                else
                {
                    Console.WriteLine("Unknown argument: " + arg);
                    PrintHelp();
                    return config;
                }
            }

            if (config.Callsign == String.Empty || config.Password == String.Empty || config.Gridsquare == String.Empty)
            {
                Console.WriteLine("ERROR: Callsign, Password, and Gridsquare are required.\n");
                PrintHelp();
                return config;
            }

            config.ValidParse = true;
            return config;
        }

        static bool IsValidIPAddress(string address)
        {
            return IPAddress.TryParse(address, out _);
        }

        static bool IsValidDNSName(string dnsName)
        {
            var dnsPattern = @"^(?=.{1,253})(?=.{1,63}(?:\.|$))(?!-)[a-zA-Z0-9-]{1,63}(?<!-)(?:\.[a-zA-Z0-9-]{1,63}(?<!-))*(?:\.|$)$";
            return Regex.IsMatch(dnsName, dnsPattern);
        }


        public static void PrintHelp()
        {
            Console.WriteLine("Usage: SOTAmatWSJTskimmer <options>");
            Console.WriteLine("  Options:");
            Console.WriteLine("       -h, --help");
            Console.WriteLine("       -v, --version");
            Console.WriteLine("       -d, --debug [default: false]");
            Console.WriteLine("       -l, --log [default: false]");
            Console.WriteLine("       -a=<DNS-name or IP-address>, --address=<name/ip> [default: 127.0.0.1]");
            Console.WriteLine("       -port=<port>, --port=<port> [default: 2237]");
            Console.WriteLine("       -m, --multicast [default: false]");
            Console.WriteLine("       -c=<SOTAmat user callsign>, --callsign=<callsign> (required)");
            Console.WriteLine("          [or use SOTAMAT_CALLSIGN environment variable]");
            Console.WriteLine("       -p=<SOTAmat user password>, --password=<SOTAmat user password> (required)");
            Console.WriteLine("          [or use SOTAMAT_PASSWORD environment variable]");
            Console.WriteLine("       -g=<gridsquare of antenna>, --gridsqure=<gridsqure of antenna> (required)");
            Console.WriteLine("          [or use SOTAMAT_GRIDSQUARE environment variable]");
            Console.WriteLine("          [NOTE: if you are remotely accessing an SDR such as WebSDR.org, don't use your home gridsquare, use the antenna's gridsquare]");
            Console.WriteLine();
            Console.WriteLine("  Examples:");
            Console.WriteLine("       SOTAmatWSJTskimmer      [Note: requires callsign, password, and gridsqure be set via environment variables]");
            Console.WriteLine("       SOTAmatWSJTskimmer -c=AA1ABC -p=mysecret -g=CM89");
            Console.WriteLine("       SOTAmatWSJTskimmer -c=AA1ABC -p=mysecret -g=CM89 -a=224.0.0.1 -port=2237 --multicast");
        }

        public static void PrintVersion()
        {
            string? version = Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString();

            // Check if version is null or empty and if so, set it to "unknown".
            version = string.IsNullOrEmpty(version) ? "unknown" : version;

            // Use the version in your code.
            Console.WriteLine($"SOTAmat WSJT-X Skimmer v{version}, by Brian Mathews AB6D,");
            Console.WriteLine("    using library WsjtxUdpLib by Tom Fanning M0LTE.");
            Console.WriteLine();
            Console.WriteLine("This utility connects to WSJT-X, filters reception reports, and sends SOTAmat messages to the SOTAmat server.");
            Console.WriteLine("More information at HTTPS://SOTAmat.com");
            Console.WriteLine();
        }

    }



}
