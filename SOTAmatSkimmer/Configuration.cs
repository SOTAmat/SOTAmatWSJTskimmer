
namespace SOTAmatSkimmer
{
    public class Configuration
    {
        public Configuration() 
        {
            Address = "127.0.0.1";
            Callsign = Environment.GetEnvironmentVariable("SOTAMAT_CALLSIGN") ?? string.Empty;
            Debug = false;
            DialFrequency = 0;
            Gridsquare = Environment.GetEnvironmentVariable("SOTAMAT_GRIDSQUARE") ?? string.Empty;
            LastHeartbeat = DateTime.Now;
            Logging = false;
            Mode = String.Empty;
            Multicast = false;
            Password = Environment.GetEnvironmentVariable("SOTAMAT_PASSWORD") ?? string.Empty;
            Port = 2237;
            ValidParse = false;
            SparkSDRmode = false;
        }
        public string Address { get; set; }
        public string Callsign { get; set; }
        public bool Debug { get; set; }
        public long DialFrequency { get; set; }
        public string Gridsquare { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public bool Logging { get; set; }
        public string Mode { get; set; }
        public bool Multicast { get; set; }
        public string Password { get; set; }
        public int Port { get; set; }
        public bool ValidParse { get; set; }
        public bool SparkSDRmode { get; set; }
    }
}
