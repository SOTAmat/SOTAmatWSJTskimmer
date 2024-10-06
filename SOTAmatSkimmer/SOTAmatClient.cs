using System.Reflection;
using System.Text.RegularExpressions;
using SOTAmatSkimmer.Utilities;

namespace SOTAmatSkimmer
{
    public static class SOTAmatClient
    {
        static bool ShowDeltaTime = true;
        public static async Task<bool> Authenticate(Configuration config)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    // Create the request content with the username and password parameters
                    var requestContent = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("username", config.Callsign),
                        new KeyValuePair<string, string>("password", config.Password),
                    });

                    // Make the POST request to the REST API and get the response
                    var response = await client.PostAsync("https://sotamat.com/wp-json/sotawp/v1/authenticate", requestContent);

                    // Check if the response status is successful (status code 200)
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                    else
                    {
                        ConsoleHelper.SafeWriteLine($"ERROR: SOTAmat Server returned an error while authenticating user.", true, ConsoleColor.Red);
                        ConsoleHelper.SafeWriteLine("Example command line:   SOTAmatSkimmer -c AB6D -p \"MyPasswordHere\" -g CN89tn\n", false);
                        // Write the response error message to the console
                        var responseContent = await response.Content.ReadAsStringAsync();
                        ConsoleHelper.SafeWriteLine(responseContent, false);
                    }
                }
            }
            catch (Exception ex)
            {
                ConsoleHelper.SafeWriteLine($"ERROR: failed to post message to SOTAmat Server when authenticating.", true, ConsoleColor.Red);
                ConsoleHelper.SafeWriteLine(ex.ToString(), false);
            }

            return false;
        }

        public static void ParseAndExecuteMessage(Configuration config, int snr, double deltaTime, string message, int deltaFrequency)
        {
            if (config.Debug) ConsoleHelper.SafeWriteLine($"Debug: Message received: {message}\n");

            // Update the average DeltaTime we are seeing from these reception reports
            if (!Console.IsOutputRedirected)
            {
                UpdateAverageDeltaTime(deltaTime);
                if (ShowDeltaTime)
                {
                    ConsoleHelper.SafeWrite($" Average DeltaTime: {deltaTimeAverage.ToString("+0.00;-0.00")}      ", false, Math.Abs(deltaTimeAverage) > 0.5 ? ConsoleColor.Red : ConsoleColor.Green, true);
                }
            }

            // If the statusMsg is a potential SOTAmat statusMsg, send it to the SOTAmat server
            string pattern = @"^(S(T(M(T)?)?|OTAM(T|AT)?)?M?)\s([0-9A-Z]{1,2}[0-9][0-9A-Z]{1,3})(/[0-9A-Z]{1,4})+$";
            Regex regex = new(pattern, RegexOptions.IgnoreCase);

            if (message.Length == 13 && regex.IsMatch(message))
            {
                ShowDeltaTime = false;
                ConsoleHelper.SafeWrite($"Sending SOTAMAT report to server: {message} =>");
                // Send the statusMsg to the SOTAmat server
                Task.Run(async () =>
                {
                    await SOTAmatClient.Send(config, snr: snr, deltaTime: deltaTime, message: message, deltaFrequency: deltaFrequency);
                    ShowDeltaTime = true;
                });
            }
        }

        public static async Task Send(Configuration config, int snr, double deltaTime, string message, int deltaFrequency)
        {
            // Send the decoded message to the SOTAmat server via HTTPS POST

            try
            {
                using (var client = new HttpClient())
                {
                    // Create the request content with the username, password, and message parameters
                    var requestContent = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("username", config.Callsign),
                        new KeyValuePair<string, string>("password", config.Password),
                        new KeyValuePair<string, string>("snr",      snr.ToString()),
                        new KeyValuePair<string, string>("deltatime",deltaTime.ToString("0.00")),
                        new KeyValuePair<string, string>("mode",     config.Mode),
                        new KeyValuePair<string, string>("message",  message),
                        new KeyValuePair<string, string>("gridsquare", config.Gridsquare),
                        new KeyValuePair<string, string>("frequency", (deltaFrequency + config.DialFrequency).ToString()), // Frequency is in Hertz.  For example: 14074950
                        new KeyValuePair<string, string>("software", $"SOTAmatSkimmer V{Assembly.GetExecutingAssembly().GetName().Version}")
                    });

                    // Make the POST request to the REST API and get the response
                    var response = await client.PostAsync("https://sotamat.com/wp-json/sotawp/v1/postmessage", requestContent);

                    // Check if the response status is successful (status code 200)
                    if (response.IsSuccessStatusCode)
                    {
                        ConsoleHelper.SafeWriteLine("POSTED.", false, ConsoleColor.Green);
                    }
                    else
                    {
                        ConsoleHelper.SafeWriteLine("ERROR!", false, ConsoleColor.Red);
                        ConsoleHelper.SafeWrite($"ERROR: SOTAmat Server returned an error. ", true, ConsoleColor.Red);
                        var responseContent = await response.Content.ReadAsStringAsync();
                        ConsoleHelper.SafeWriteLine(responseContent, false, ConsoleColor.Yellow);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                ConsoleHelper.SafeWriteLine("ERROR!", false, ConsoleColor.Red);
                ConsoleHelper.SafeWriteLine("ERROR: Excelption posting to SOTAmat Server.", true, ConsoleColor.Red);
                ConsoleHelper.SafeWriteLine(ex.ToString(), false, ConsoleColor.Yellow);
                return;
            }
        }

        // Create a circular buffer for tracking DeltaTime values
        private static readonly object _lockObject = new object();
        private const int DELTA_TIME_REPORTS_TO_AVERAGE = 100;
        private static readonly CircularBuffer<double> deltaTimeBuffer = new(DELTA_TIME_REPORTS_TO_AVERAGE);
        private static double deltaTimeAverage = 0.0;
        private static double deltaTimeAccumulator = 0.0;


        private static double UpdateAverageDeltaTime(double deltaTime)
        {
            lock (_lockObject)
            {
                if (deltaTimeBuffer.IsFull())
                    deltaTimeAccumulator -= deltaTimeBuffer.Dequeue();
                deltaTimeBuffer.Enqueue(deltaTime);
                deltaTimeAccumulator += deltaTime;

                deltaTimeAverage = deltaTimeAccumulator / (double)deltaTimeBuffer.Count;
                return deltaTimeAverage;
            }
        }


    }
}
