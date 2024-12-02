using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.PhonePe
{
    /// <summary>
    /// Represents settings of the PayPal Standard payment plugin
    /// </summary>
    public class PhonePePaymentSettings : ISettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether to use sandbox (testing environment)
        /// </summary>
        public bool UseSandbox { get; set; }

        public string SandboxURL { get; set; }

        public string ProductionURL { get; set; }

        public string MerchantId { get; set; }

        public string Salt { get; set; }
    }
}
