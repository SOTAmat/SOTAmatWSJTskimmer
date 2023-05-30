
namespace SOTAmatSkimmer
{
    class Program
    {


        static int Main(string[] args)
        {

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

            Console.WriteLine("[type CTRL-C to exit]\n");
            Console.WriteLine($"Attempting to connect {config.Callsign} to {(config.SparkSDRmode ? "SparkSDR" : "WSJT-X")} via {(config.Multicast ? "multicast" : "direct")} UDP at address {config.Address} and a receiving antenna gridsquare of {config.Gridsquare}...\n");


            if (config.SparkSDRmode)
            { 
                SparkSDRlooper myLooper = new(config);
                return myLooper.Loop();
            }
            else
            {
                WsjtxLooper myLooper = new(config);
                return myLooper.Loop();
            }
        }

    }
}
