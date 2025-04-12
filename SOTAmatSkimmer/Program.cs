namespace SOTAmatSkimmer
{
    class Program
    {


        static int Main(string[] args)
        {

            ArgumentParser.PrintVersion();

            // Parse the args passed in and set the variables
            Configuration config = ArgumentParser.Parse(args);

            // If showparams flag is set, display all parameters
            if (config.ShowParams)
            {
                Console.WriteLine("\n--- Configuration Parameters ---");
                Console.WriteLine($"Callsign: '{config.Callsign}'");
                Console.WriteLine($"Password: '{config.Password}'");
                Console.WriteLine($"Gridsquare: '{config.Gridsquare}'");
                Console.WriteLine($"Address: {config.Address}");
                Console.WriteLine($"Port: {config.Port}");
                Console.WriteLine($"Multicast: {config.Multicast}");
                Console.WriteLine($"SparkSDR Mode: {config.SparkSDRmode}");
                Console.WriteLine($"Debug: {config.Debug}");
                Console.WriteLine($"Logging: {config.Logging}");
                Console.WriteLine($"Heartbeat Timeout: {config.HeartbeatTimeoutSeconds} seconds");
                Console.WriteLine($"Reconnect Interval: {config.ReconnectIntervalSeconds} seconds");
                Console.WriteLine("-------------------------------\n");
            }

            // Validate that we have reasonable instructions and that the user credentials are good
            if (config.ValidParse == false || SOTAmatClient.Authenticate(config).Result != true)
            {
                Console.WriteLine();
                Console.WriteLine("Enter a key to exit...");
                Console.ReadKey();
                return 2;
            }

            // Great.  With the bookeeping out of the way, let's connect to WSJT-X or SparkSDR and start listening
            Console.WriteLine("[type CTRL-C to exit]\n");

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
