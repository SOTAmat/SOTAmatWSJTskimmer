using System.Reflection;
using CommandLine;

namespace SOTAmatSkimmer
{

    public class ArgumentParser
    {
        public static Configuration Parse(string[] args)
        {
            var config = new Configuration();

            var parser = new Parser(settings =>
            {
                settings.AutoVersion = false;
                settings.AutoHelp = true;
                settings.HelpWriter = Console.Out;
                settings.EnableDashDash = true;
            });

            // Pre-process args to support both '=' and space separators
            var processedArgs = new List<string>();
            foreach (var arg in args)
            {
                if (arg.Contains('='))
                {
                    var parts = arg.Split('=');
                    processedArgs.Add(parts[0]);
                    processedArgs.Add(parts[1]);
                }
                else
                {
                    processedArgs.Add(arg);
                }
            }

            var result = parser.ParseArguments<Configuration>(processedArgs);

            result
                .WithParsed(parsedArgs => config = parsedArgs)
                .WithNotParsed(errs =>
                {
                    HandleParseError(errs);
                    Environment.Exit(1); // Exit the program if there are parsing errors
                });

            // Validate configuration
            if (string.IsNullOrEmpty(config.Callsign) || string.IsNullOrEmpty(config.Password) || string.IsNullOrEmpty(config.Gridsquare))
            {
                Console.WriteLine("ERROR: Callsign, Password, and Gridsquare are required. Try the '--help' command line option for instructions.\n");
                PrintHelp();
                Environment.Exit(1); // Exit the program if required fields are missing
            }

            config.ValidParse = true;
            return config;
        }

        static void HandleParseError(IEnumerable<Error> errs)
        {
            Console.WriteLine("Errors occurred while parsing command line arguments:");
            foreach (var error in errs)
            {
                Console.WriteLine($"- {error.Tag}: {error}");
            }
            PrintHelp();
        }
        public static void PrintHelp()
        {
            return;

            //Console.WriteLine("Usage: SOTAmatSkimmer <options>");
            //Console.WriteLine("  Options:");
            //Console.WriteLine("       -h, --help");
            //Console.WriteLine("       -v, --version");
            //Console.WriteLine("       -d, --debug [default: false]");
            //Console.WriteLine("       -l, --log [default: false]");
            //Console.WriteLine("       -a=<DNS-name or IP-address>, --address=<name/ip> [default: 127.0.0.1]");
            //Console.WriteLine("       -port=<port>, --port=<port> [default: 2237]");
            //Console.WriteLine("       -m, --multicast [default: false]");
            //Console.WriteLine("       -c=<SOTAmat user callsign>, --callsign=<callsign> (required)");
            //Console.WriteLine("          [or use SOTAMAT_CALLSIGN environment variable]");
            //Console.WriteLine("       -p=<SOTAmat user password>, --password=<SOTAmat user password> (required)");
            //Console.WriteLine("          [or use SOTAMAT_PASSWORD environment variable]");
            //Console.WriteLine("       -g=<gridsquare of antenna>, --gridsqure=<gridsqure of antenna> (required)");
            //Console.WriteLine("          [or use SOTAMAT_GRIDSQUARE environment variable]");
            //Console.WriteLine("          [NOTE: if you are remotely accessing an SDR such as WebSDR.org, don't use your home gridsquare, use the antenna's gridsquare]");
            //Console.WriteLine("       -sparksdr");
            //Console.WriteLine("          [Use to connect to a SparkSDR server using websockets rather than WSJT-X UDP]");
            //Console.WriteLine("          [Note that SparkSDR might only work on localhost with the default -a=127.0.0.1]");
            //Console.WriteLine();
            //Console.WriteLine("  Examples:");
            //Console.WriteLine("       SOTAmatSkimmer      [Note: requires callsign, password, and gridsqure be set via environment variables]");
            //Console.WriteLine("       SOTAmatSkimmer -c=AA1ABC -p=mysecret -g=CM89");
            //Console.WriteLine("       SOTAmatSkimmer -c=AA1ABC -p=mysecret -g=CM89 -a=224.0.0.1 -port=2237 --multicast");
        }

        public static void PrintVersion()
        {
            string? version = Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString();

            // Check if version is null or empty and if so, set it to "unknown".
            version = string.IsNullOrEmpty(version) ? "unknown" : version;

            // Use the version in your code.
            Console.WriteLine($"SOTAmatSkimmer v{version}, Copyright (c) 2023-2024 Brian Mathews, AB6D. Licensed under The MIT License.");
            Console.WriteLine("     Uses library WsjtxUdpLib by Tom Fanning M0LTE,");
            Console.WriteLine("     Uses library CommandLineParser, (c) Giacomo Stelluti Scala & Contributors. The MIT License.");
            Console.WriteLine("     Uses library Newtonsoft.Json, (c) James Newton-King. The MIT License.");
            Console.WriteLine("     Uses library WebSocket4Net, (c) Kerry Jiang. The Apache V2.0 License.");

            Console.WriteLine();
            Console.WriteLine("This utility connects to either a WSJT-X or a SparkSDR server, filters reception reports, ");
            Console.WriteLine("   and sends SOTAmat messages to the SOTAmat server.  Information at https://SOTAmat.com");
            Console.WriteLine("");
            Console.WriteLine();
        }

    }
}
