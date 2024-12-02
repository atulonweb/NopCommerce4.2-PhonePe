using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.PhonePe.Models
{
    public class ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PhonePe.Fields.UseSandbox")]
        public bool UseSandbox { get; set; }
        public bool UseSandbox_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PhonePe.Fields.SandboxURL")]
        public string SandboxURL { get; set; }
        public bool SandboxURL_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PhonePe.Fields.ProductionURL")]
        public string ProductionURL { get; set; }
        public bool ProductionURL_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PhonePe.Fields.MerchantId")]
        public string MerchantId { get; set; }
        public bool MerchantId_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PhonePe.Fields.Salt")]
        public string Salt { get; set; }
        public bool Salt_OverrideForStore { get; set; }
    }
}