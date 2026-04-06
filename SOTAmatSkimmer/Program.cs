namespace SOTAmatSkimmer
{
    class Program
    {
        static int Main(string[] args)
        {
            Configuration config = ArgumentParser.Parse(args);

            if (config.WorkerMode)
            {
                return RunWorker(config);
            }

            ArgumentParser.PrintVersion();

            if (config.ShowParams)
            {
                ShowConfig(config);
            }

            WorkerSupervisor supervisor = new(config, args);
            return supervisor.Run();
        }

        private static void ShowConfig(Configuration config)
        {
            Console.WriteLine("\n--- Configuration Parameters ---");
            Console.WriteLine($"Callsign: '{config.Callsign}'");
            Console.WriteLine($"Password: '{config.Password}'");
            Console.WriteLine($"Gridsquare: '{config.Gridsquare}'");
            Console.WriteLine($"Address: {config.Address}");
            Console.WriteLine($"Port: {config.Port}");
            Console.WriteLine($"Multicast: {config.Multicast}");
            Console.WriteLine($"Multicast Interface: '{config.MulticastInterface}'");
            Console.WriteLine($"SparkSDR Mode: {config.SparkSDRmode}");
            Console.WriteLine($"Debug: {config.Debug}");
            Console.WriteLine($"Logging: {config.Logging}");
            Console.WriteLine($"Heartbeat Timeout: {config.HeartbeatTimeoutSeconds} seconds");
            Console.WriteLine($"Reconnect Interval: {config.ReconnectIntervalSeconds} seconds");
            Console.WriteLine("Process Isolation: enabled");
            Console.WriteLine("Worker Heartbeat Timeout: 60 seconds");
            Console.WriteLine("Worker Source Stale Timeout: WSJT-X 120 seconds / SparkSDR disabled");
            Console.WriteLine("Max Worker Restarts: 10 within 300 seconds");
            Console.WriteLine("Worker Restart Backoff: 1, 2, 5, 10, 20 seconds");
            Console.WriteLine("-------------------------------\n");
        }

        private static int RunWorker(Configuration config)
        {
            WorkerHealthState.Reset(config.SparkSDRmode ? "sparksdr" : "wsjt");
            WorkerIpcReporter? reporter = null;
            try
            {
                if (config.ShowParams)
                {
                    ShowConfig(config);
                }

                reporter = WorkerIpcReporter.Start(config);
                if (!ValidateAndAuthenticate(config, pauseOnFailure: false))
                {
                    WorkerHealthState.RecordError();
                    reporter?.PublishEvent("auth-failed", "Worker authentication failed.");
                    return 2;
                }

                reporter?.PublishEvent("worker-ready", "Worker authenticated and entering adapter loop.");
                return RunLooper(config);
            }
            catch (Exception ex)
            {
                WorkerHealthState.RecordError();
                reporter?.PublishEvent("worker-fatal", ex.Message);
                return 1;
            }
            finally
            {
                reporter?.Dispose();
            }
        }

        private static bool ValidateAndAuthenticate(Configuration config, bool pauseOnFailure)
        {
#if DEBUG
            if (config.TestSkipAuth)
            {
                return true;
            }
#endif

            if (config.ValidParse && SOTAmatClient.Authenticate(config).Result)
            {
                return true;
            }

            if (pauseOnFailure)
            {
                Console.WriteLine();
                Console.WriteLine("Enter a key to exit...");
                Console.ReadKey();
            }

            return false;
        }

        private static int RunLooper(Configuration config)
        {
            if (config.SparkSDRmode)
            {
                SparkSDRlooper myLooper = new(config);
                return myLooper.Loop();
            }

            WsjtxLooper wsjtLooper = new(config);
            return wsjtLooper.Loop();
        }
    }
}
