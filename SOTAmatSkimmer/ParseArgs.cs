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
            });

            parser.ParseArguments<Configuration>(args)
                .WithParsed(parsedArgs => config = parsedArgs)
                .WithNotParsed(errs => HandleParseError(errs));

            // Validate configuration
            if (config.Callsign == String.Empty || config.Password == String.Empty || config.Gridsquare == String.Empty)
            {
                Console.WriteLine("ERROR: Callsign, Password, and Gridsquare are required.  Try the '--help' command line option for instructions.\n");
                PrintHelp();
            }
            else
            {
                config.ValidParse = true;
            }
            return config;
        }

        static void HandleParseError(IEnumerable<Error> errs)
        {
            // In case we have any errors while parsing arguments, we can handle them here.
            // You can print the errors, or throw an exception, etc.
            foreach (var error in errs)
            {
                Console.WriteLine($"Error parsing arguments: {error.Tag}");
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
            Console.WriteLine($"SOTAmatSkimmer v{version}, Copyright (c) 2023 Brian Mathews, AB6D. Licensed under The MIT License.");
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
