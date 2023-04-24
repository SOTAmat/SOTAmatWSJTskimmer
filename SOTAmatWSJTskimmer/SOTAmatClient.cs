using M0LTE.WsjtxUdpLib.Messages.Out;

namespace SOTAmatWSJTskimmer
{
    public class SOTAmatClient
    {
        public async static void Send(Configuration config, DecodeMessage message)
        {
            // Send the message to the SOTAmat server via HTTPS POST

            try
            {
                using (var client = new HttpClient())
                {
                    // Create the request content with the username and password parameters
                    var requestContent = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("username", config.Callsign),
                        new KeyValuePair<string, string>("password", config.Password),
                        new KeyValuePair<string, string>("snr",      message.Snr.ToString()),
                        new KeyValuePair<string, string>("deltatime",message.DeltaTime.ToString()),
                        new KeyValuePair<string, string>("mode",     config.Mode),
                        new KeyValuePair<string, string>("message",  message.Message),
                        new KeyValuePair<string, string>("gridsquare", config.Gridsquare),
                        new KeyValuePair<string, string>("frequency", (message.DeltaFrequency + config.DialFrequency).ToString()),
                        new KeyValuePair<string, string>("software", "SOTAmat-WSJT-skim V1.0")
                    }); 

                    // Make the POST request to the REST API and get the response
                    var response = await client.PostAsync("https://sotamat.com/wp-json/sotawp/v1/postmessage", requestContent);

                    // Check if the response status is successful (status code 200)
                    if (response.IsSuccessStatusCode)
                    {
                        // Write the response content to the console
                        var responseContent = await response.Content.ReadAsStringAsync();
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
            catch (Exception)
            {
                Console.WriteLine("ERROR: failed to post message to SOTAmat Server.");
            }


        }
    }
}
