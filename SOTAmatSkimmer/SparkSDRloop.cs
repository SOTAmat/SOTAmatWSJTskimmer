using Newtonsoft.Json;
using System.Collections.Concurrent;
using WebSocket4Net;
using SOTAmatSkimmer.Utilities;

namespace SOTAmatSkimmer
{
    public class SparkSDRlooper
    {
        Configuration Config { get; set; }
        WebSocketClient wsClient;
        private string _url;

        // Set up the class constructor
        public SparkSDRlooper(Configuration config)
        {
            Config = config;
            _url = $"ws://{Config.Address}:{Config.Port}/Spark";
            wsClient = new WebSocketClient(Config, _url);
        }
        public int Loop()
        {
            wsClient.Start();

            // Continuously consume response queue
            while (true)
            {
                while (!wsClient.ResponseQueue.IsEmpty)
                {
                    if (wsClient.ResponseQueue.TryDequeue(out string? message))
                    {
                        var jsonData = JsonConvert.DeserializeObject<dynamic>(message);

                        if (jsonData?.spots != null)
                        {
                            foreach (var spot in jsonData.spots)
                            {
                                try
                                {
                                    Config.LastHeartbeat = DateTime.Now;

                                    int mySnr = (int)spot["snr"].Value;
                                    double myDeltaTime = spot["dt"].Value;
                                    string myMessage = spot["msg"].Value;
                                    double myTunedFrequency = spot["tunedfrequency"].Value;
                                    double myFrequency = spot["frequency"].Value;
                                    int myDeltaFrequency = (int)(myFrequency - myTunedFrequency);
                                    Config.DialFrequency = (long)myTunedFrequency;
                                    Config.Mode = spot["mode"].Value;

                                    SOTAmatClient.ParseAndExecuteMessage(Config,
                                                                            snr: mySnr,
                                                                            deltaTime: myDeltaTime,
                                                                            message: myMessage,
                                                                            deltaFrequency: myDeltaFrequency);

                                }
                                catch (Exception e)
                                {
                                    ConsoleHelper.SafeWriteLine($"ERROR: Unable to extract required SparkSDR message parameters: {e.Message}\n", true, ConsoleColor.Red);
                                }

                            }
                        }
                        else
                        {
                            ConsoleHelper.SafeWriteLine($"WARNING: 'spots' field is missing in the received message.\n", true, ConsoleColor.Yellow);
                        }
                    }
                }

                Thread.Sleep(1000);
            }

        }

        public void Dispose()
        {
            wsClient.Stop();
            // Dispose other managed resources if any
        }
    }


    // ==================================================================================================
    // ==================================================================================================
    // ==================================================================================================

    public class WebSocketClient
    {
        private WebSocket webSocket;
        public ConcurrentQueue<string> ResponseQueue { get; } = new ConcurrentQueue<string>();

        private Configuration Config { get; set; }
        private string Url { get; set; }
        public WebSocketClient(Configuration config, string url)
        {
            webSocket = new WebSocket(url);
            webSocket.Opened += WebSocket_Opened;
            webSocket.Closed += WebSocket_Closed;
            webSocket.Error += WebSocket_Error;
            webSocket.MessageReceived += WebSocket_MessageReceived;
            Config = config;
            Url = url;
        }

        public void Start()
        {
            Console.WriteLine($"Connecting {Config.Callsign} to {(Config.SparkSDRmode ? "SparkSDR" : "WSJT-X")} via {(Config.Multicast ? "multicast" : "direct")} {(Config.SparkSDRmode ? "websocket" : "UDP")} at {Url} with grid {Config.Gridsquare}:\n");
            Console.WriteLine();

            webSocket.Open();
        }

        public void Stop()
        {
            webSocket.Close();
        }

        private void WebSocket_Opened(object? sender, EventArgs e)
        {
            ConsoleHelper.SafeWriteLine($"SparkSDR connection established.\n", true, ConsoleColor.Green);

            // Subscribe to spots
            webSocket.Send("{\"cmd\":\"subscribeToSpots\",\"Enable\":true}");
        }

        private void WebSocket_Closed(object? sender, EventArgs e)
        {
            ConsoleHelper.SafeWriteLine($"SparkSDR connection lost. Attempting reconnect in 15 sec.\n", true, ConsoleColor.Red);
            Task.Delay(15000).ContinueWith(_ => Start());
        }

        private void WebSocket_Error(object? sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            ConsoleHelper.SafeWriteLine($"SparkSDR connect error (is it running?): {e.Exception.Message}\n", true, ConsoleColor.Red);
        }

        private void WebSocket_MessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            ResponseQueue.Enqueue(e.Message);
        }
    }
}
