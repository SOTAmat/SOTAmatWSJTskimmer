using M0LTE.WsjtxUdpLib.Messages.Out;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Threading;
using WebSocket4Net;

namespace SOTAmatSkimmer
{
    public class SparkSDRlooper
    {
        Configuration Config { get; set; }
        WebSocketClient wsClient;
        private string url;

        // Set up the class constructor
        public SparkSDRlooper(Configuration config)
        {
            Config = config;
            url = $"ws://{Config.Address}:{Config.Port}/Spark";
            wsClient = new WebSocketClient(url);
        }
        public int Loop()
        {
            SOTAmatClient smClient = new();

            Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} Connecting to {url}...\n");

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

                                    smClient.ParseAndExecuteMessage(Config,
                                                                                snr: mySnr,
                                                                                deltaTime: myDeltaTime,
                                                                                message: myMessage,
                                                                                deltaFrequency: myDeltaFrequency);

                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} ERROR: Unable to extract required SparkSDR message parameters: {e.Message}");
                                }

                            }
                        }
                        else
                        {
                            Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} WARNING: 'spots' field is missing in the received message.");
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

        public WebSocketClient(string url)
        {
            webSocket = new WebSocket(url);
            webSocket.Opened += WebSocket_Opened;
            webSocket.Closed += WebSocket_Closed;
            webSocket.Error += WebSocket_Error;
            webSocket.MessageReceived += WebSocket_MessageReceived;
        }

        public void Start()
        {
            webSocket.Open();
        }

        public void Stop()
        {
            webSocket.Close();
        }

        private void WebSocket_Opened(object? sender, EventArgs e)
        {
            Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} SparkSDR connection established.");

            // Subscribe to spots
            webSocket.Send("{\"cmd\":\"subscribeToSpots\",\"Enable\":true}");
        }

        private void WebSocket_Closed(object? sender, EventArgs e)
        {
            Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} SparkSDR connection closed. Attempting to reconnect in 15 seconds...");
            Task.Delay(15000).ContinueWith(_ => Start());
        }

        private void WebSocket_Error(object? sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} SparkSDR connect error (is it running?): {e.Exception.Message}");
        }

        private void WebSocket_MessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            ResponseQueue.Enqueue(e.Message);
        }
    }
}
