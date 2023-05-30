using System.Reflection;
using System.Text.RegularExpressions;

namespace SOTAmatSkimmer
{
    public class SOTAmatClient
    {
        public async static Task<bool> Authenticate(Configuration config)
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
                        Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} ERROR: SOTAmat Server returned an error while authenticating user.");
                        Console.WriteLine("Example command line:   SOTAmatSkimmer -c AB6D -p \"MyPasswordHere\" -g CN89tn\n");
                        // Write the response error message to the console
                        var responseContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine(responseContent);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} ERROR: failed to post message to SOTAmat Server when authenticating.");
                Console.WriteLine(ex.ToString());
            }

            return false;
        }

        public static void ParseAndExecuteMessage(Configuration config, int snr, double deltaTime, string message, int deltaFrequency)
        {
            if (config.Debug) Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm:ss")} Debug: Message received: {message}");

            // If the statusMsg is a potential SOTAmat statusMsg, send it to the SOTAmat server
            string pattern = @"^(S(T(M(T)?)?|OTAM(T|AT)?)?M?)\s([0-9A-Z]{1,2}[0-9][0-9A-Z]{1,3})(/[0-9A-Z]{1,4})+$";
            Regex regex = new(pattern, RegexOptions.IgnoreCase);

            if (message.Length == 13 && regex.IsMatch(message))
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}: SOTAmat message received, sending to server: {message}");
                // Send the statusMsg to the SOTAmat server
                Task.Run(() => SOTAmatClient.Send(config, snr: snr, deltaTime: deltaTime, message: message, deltaFrequency: deltaFrequency));
            }
        }
        public async static void Send(Configuration config, int snr, double deltaTime, string message, int deltaFrequency)
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
                        new KeyValuePair<string, string>("frequency", (deltaFrequency + config.DialFrequency).ToString()),
                        new KeyValuePair<string, string>("software", $"SOTAmatSkimmer V{Assembly.GetExecutingAssembly().GetName().Version}")
                    }); 

                    // Make the POST request to the REST API and get the response
                    var response = await client.PostAsync("https://sotamat.com/wp-json/sotawp/v1/postmessage", requestContent);

                    // Check if the response status is successful (status code 200)
                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        if (config.Debug)
                        {
                            Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm:ss")} SOTAmat server responded with: success!");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} ERROR: SOTAmat Server returned an error while posting a potential SOTAmat message. ");
                        // Write the response error message to the console
                        var responseContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine(responseContent);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTime.Now.ToString("MM-dd HH:mm")} ERROR: unspecific failure to post message to SOTAmat Server.");
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
