using M0LTE.WsjtxUdpLib.Messages.Out;
using System.Reflection;

namespace SOTAmatWSJTskimmer
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
                        Console.Write($"ERROR: SOTAmat Server returned an error while authenticating user.");
                        // Write the response error message to the console
                        var responseContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine(responseContent);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: failed to post message to SOTAmat Server when authenticating.");
                Console.WriteLine(ex.ToString());
            }

            return false;
        }

        public async static void Send(Configuration config, DecodeMessage message)
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
                        new KeyValuePair<string, string>("snr",      message.Snr.ToString()),
                        new KeyValuePair<string, string>("deltatime",message.DeltaTime.ToString("0.00")),
                        new KeyValuePair<string, string>("mode",     config.Mode),
                        new KeyValuePair<string, string>("message",  message.Message),
                        new KeyValuePair<string, string>("gridsquare", config.Gridsquare),
                        new KeyValuePair<string, string>("frequency", (message.DeltaFrequency + config.DialFrequency).ToString()),
                        new KeyValuePair<string, string>("software", $"SOTAmat-WSJT-skim V{Assembly.GetExecutingAssembly().GetName().Version}")
                    }); 

                    // Make the POST request to the REST API and get the response
                    var response = await client.PostAsync("https://sotamat.com/wp-json/sotawp/v1/postmessage", requestContent);

                    // Check if the response status is successful (status code 200)
                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        if (config.Debug)
                        {
                            Console.WriteLine("SOTAmat server responded with: success!");
                        }
                    }
                    else
                    {
                        Console.Write($"ERROR: SOTAmat Server returned an error while posting a potential SOTAmat message. ");
                        // Write the response error message to the console
                        var responseContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine(responseContent);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: failed to post message to SOTAmat Server.");
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
