
namespace SOTAmatSkimmer
{
    class Program
    {


        static int Main(string[] args)
        {

            ArgumentParser.PrintVersion();

            // Parse the args passed in and set the variables
            Configuration config = ArgumentParser.Parse(args);

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
            Console.WriteLine($"Connect {config.Callsign} to {(config.SparkSDRmode ? "SparkSDR" : "WSJT-X")} via {(config.Multicast ? "multicast" : "direct")} {(config.SparkSDRmode ? "websocket" : "UDP")} at address {config.Address} with antenna gridsquare {config.Gridsquare}:\n");
            Console.WriteLine();

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
