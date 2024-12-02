using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.PhonePe.Components
{
    [ViewComponent(Name = "PaymentPhonePe")]
    public class PaymentPhonePeViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            return View("~/Plugins/Payments.PhonePe/Views/PaymentInfo.cshtml");
        }
    }
}
