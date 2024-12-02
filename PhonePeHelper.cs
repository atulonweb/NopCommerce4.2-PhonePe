using Nop.Core.Domain.Payments;
using System;
using System.Security.Cryptography;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Text;

namespace Nop.Plugin.Payments.PhonePe
{
    /// <summary>
    /// Represents PhonePe helper
    /// </summary>
    public class PhonePeHelper
    {
        #region Properties

        /// <summary>
        /// Get nopCommerce partner code
        /// </summary>
        public static string NopCommercePartnerCode => "nopCommerce_SP";

        /// <summary>
        /// Get the generic attribute name that is used to store an order total that actually sent to PhonePe (used to PDT order total validation)
        /// </summary>
        public static string OrderTotalSentToPhonePe => "OrderTotalSentToPhonePe";

        #endregion

        #region Methods



        public static string SignRequest(string payload)
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

        public static string sha256_hash(String value)
        {
            StringBuilder Sb = new StringBuilder();

            using (SHA256 hash = SHA256Managed.Create())
            {
                Encoding enc = Encoding.UTF8;
                Byte[] result = hash.ComputeHash(enc.GetBytes(value));

                foreach (Byte b in result)
                    Sb.Append(b.ToString("x2"));
            }

            return Sb.ToString();
        }
        /// <summary>
        /// Gets a payment status
        /// </summary>
        /// <param name="paymentStatus">PhonePe payment status</param>
        /// <param name="pendingReason">PhonePe pending reason</param>
        /// <returns>Payment status</returns>
        /// 
        public static PaymentStatus GetPaymentStatus(string paymentStatus, string pendingReason)
        {
            var result = PaymentStatus.Pending;

            if (paymentStatus == null)
                paymentStatus = string.Empty;

            if (pendingReason == null)
                pendingReason = string.Empty;

            switch (paymentStatus.ToUpper())
            {
                case "PAYMENT_PENDING":
                    if (pendingReason.Equals("authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        result = PaymentStatus.Authorized;
                    }
                    else
                    {
                        result = PaymentStatus.Pending;
                    }
                    break;
                case "PAYMENT_SUCCESS":
                    result = PaymentStatus.Paid;
                    break;
                default:
                    break;
            }

            return result;
        }

        #endregion
    }

    public partial class ResponseData
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data")]
        public Data Data { get; set; }
    }

    public partial class Data
    {
        [JsonProperty("merchantId")]
        public string MerchantId { get; set; }

        [JsonProperty("merchantTransactionId")]
        public string MerchantTransactionId { get; set; }

        [JsonProperty("instrumentResponse")]
        public InstrumentResponse InstrumentResponse { get; set; }
    }

    public partial class InstrumentResponse
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("redirectInfo")]
        public RedirectInfo RedirectInfo { get; set; }
    }

    public partial class RedirectInfo
    {
        [JsonProperty("url")]
        public Uri Url { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }
    }

}