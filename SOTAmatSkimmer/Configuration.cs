using CommandLine;

namespace SOTAmatSkimmer
{
    public class Configuration
    {
        private bool _sparkSDRmode = false;
        public Configuration()
        {
        }

        // Configuration properties not handled by CommandLineParser
        public long DialFrequency { get; set; } = 0;
        public DateTime LastHeartbeat { get; set; } = DateTime.Now;
        public string Mode { get; set; } = String.Empty;

        // Command line Configuration options handled by CommandLineParser
        public bool ValidParse { get; set; } = false;

        [Option('a', "address", Required = false, HelpText = "IP address of the server (WSJT-X or SparkSDR). Default: 127.0.0.1")]
        public string Address { get; set; } = "127.0.0.1";

        [Option('c', "callsign", Required = false, HelpText = "Your callsign (and www.SOTAmat.com username). Required. Can also be set using SOTAMAT_CALLSIGN environment variable.")]
        public string Callsign { get; set; } = (Environment.GetEnvironmentVariable("SOTAMAT_CALLSIGN") ?? string.Empty);

        [Option('d', "debug", Required = false, HelpText = "Print additional debugging information while running. Default: False")]
        public bool Debug { get; set; } = false;

        [Option('g', "gridsquare", Required = false, HelpText = "The 6-character Gridsquare of your antenna location. Required. Can also be set using SOTAMAT_GRIDSQUARE environment variable.")]
        public string Gridsquare { get; set; } = (Environment.GetEnvironmentVariable("SOTAMAT_GRIDSQUARE") ?? string.Empty);

        [Option('l', "log", Required = false, HelpText = "Enable reception report logging to a log file. Default: False")]
        public bool Logging { get; set; } = false;

        [Option('m', "multicast", Required = false, HelpText = "Connect to the server via Multicast protocol. Default: False")]
        public bool Multicast { get; set; } = false;

        [Option('p', "password", Required = false, HelpText = "Your password on www.SOTAmat.com. Can also be set using SOTAMAT_PASSWORD environment variable.")]
        public string Password { get; set; } = (Environment.GetEnvironmentVariable("SOTAMAT_PASSWORD") ?? string.Empty);

        [Option("port", Required = false, HelpText = "Connect to the server via this network port. Default: 2237")]
        public int Port { get; set; } = 2237;

        [Option('s', "sparksdr", Required = false, HelpText = "Connect to a SparkSDR server rather than WSJT-X server and update port to 4649. Default: False")]
        public bool SparkSDRmode
        {
            get
            {
                return _sparkSDRmode;
            }
            set
            {
                _sparkSDRmode = value;
                if (_sparkSDRmode)
                {
                    Port = 4649;
                }
            }
        }

        // Command line verb actions handled by CommandLineParser
        [Option('v', "version", Required = false, HelpText = "Show version information.")]
        public bool Version { get; set; } = false;

        [Option("heartbeat-timeout", Required = false, HelpText = "Expected connection heartbeat from WSJT-X/SparkSDR timeout in seconds. Default: 30")]
        public int HeartbeatTimeoutSeconds { get; set; } = 30;

        [Option("reconnect-interval", Required = false, HelpText = "Reconnection attempt interval in seconds to WSJT-X/SparkSDR after a network error. Default: 15")]
        public int ReconnectIntervalSeconds { get; set; } = 15;

    }
}