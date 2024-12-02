using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Net.Http.Headers;
using Nop.Core;

namespace Nop.Plugin.Payments.PhonePe.Services
{
    /// <summary>
    /// Represents the HTTP client to request PayPal services
    /// </summary>
    public partial class PhonePeHttpClient
    {
        #region Fields

        private readonly HttpClient _httpClient;
        private readonly PhonePePaymentSettings _phonePePaymentSettings;

        #endregion

        #region Ctor

        public PhonePeHttpClient(HttpClient client,
            PhonePePaymentSettings phonePePaymentSettings)
        {
            //configure client
            client.Timeout = TimeSpan.FromMilliseconds(5000);
            client.DefaultRequestHeaders.Add(HeaderNames.UserAgent, $"nopCommerce-{NopVersion.CurrentVersion}");

            _httpClient = client;
            _phonePePaymentSettings = phonePePaymentSettings;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets PDT details
        /// </summary>
        /// <param name="tx">TX</param>
        /// <returns>The asynchronous task whose result contains the PDT details</returns>
        public async Task<string> GetPdtDetailsAsync(string orderId)
        {

            int[] intervals = { 10000, 3000, 6000, 10000, 30000, 60000 }; // Intervals in milliseconds
            int totalTimeout = 2 * 60 * 1000; // 2 minutes in milliseconds

            DateTime startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < totalTimeout)
            {
                foreach (int interval in intervals)
                {
                    await Task.Delay(interval);
                    //get response            
                    Task<string> result = MakeRequest(orderId);

                    // This will block the main thread until the task is completed
                    result.Wait();
                    var statusCheckContent = result.Result;
                    if (!string.IsNullOrEmpty(statusCheckContent) && !statusCheckContent.ToLower().Contains("pending"))
                    {
                        Console.WriteLine("Payment status is no longer Pending.");
                        return result.Result;
                    }
                    // Continue checking if the status is still Pending
                }
            }

            Console.WriteLine("Status checks timed out. Payment status is still Pending.");

            return "Pending";
        }

        string SignRequest(string payload)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
                byte[] hashBytes = sha256.ComputeHash(payloadBytes);

                // Convert the hash to a hexadecimal string
                StringBuilder hashStringBuilder = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    hashStringBuilder.Append(b.ToString("x2"));
                }

                return hashStringBuilder.ToString();
            }
        }

        public async Task<string> MakeRequest(string orderId)
        {
            try
            {
                // ON LIVE URL YOU MAY GET CORS ISSUE, ADD Below LINE TO RESOLVE
                ServicePointManager.Expect100Continue = true;
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                var phonePeGatewayURL = _phonePePaymentSettings.UseSandbox ?
                   _phonePePaymentSettings.SandboxURL :
                   _phonePePaymentSettings.ProductionURL;

                var httpClient = new HttpClient();
                var uri = new Uri($"{phonePeGatewayURL}/pg/v1/status/" + _phonePePaymentSettings.MerchantId + "/" + orderId);
                string s = "/pg/v1/status/" + _phonePePaymentSettings.MerchantId + "/" + orderId + _phonePePaymentSettings.Salt;
                var xVerify = SignRequest(s) + "###" + 1;
                // Add headers
                httpClient.DefaultRequestHeaders.Add("accept", "application/json");
                httpClient.DefaultRequestHeaders.Add("X-VERIFY", xVerify);
                httpClient.DefaultRequestHeaders.Add("X-MERCHANT-ID", _phonePePaymentSettings.MerchantId);//atul

                // Create JSON request body

                // Send POST request
                var response = await httpClient.GetAsync(uri);
                response.EnsureSuccessStatusCode();

                // Read and deserialize the response content
                var responseContent = response.Content.ReadAsStringAsync().Result;

                // Return a response
                return responseContent;
            }
            catch (Exception ex)
            {
                // Handle errors and return an error response
                return null;
            }
        }


        #endregion
    }
}