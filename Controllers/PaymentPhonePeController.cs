using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.PhonePe.Models;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.PhonePe.Controllers
{
    public class VerifyRequestModel
    {
        public string X_VERIFY { get; set; }
        public string base64 { get; set; }
        public string TransactionId { get; set; }
        public string MERCHANTID { get; set; }
        // Add other properties from the request if needed
    }
    public class PaymentPhonePeController : BasePaymentController
    {
        #region Fields

        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly IPaymentPluginManager _paymentPluginManager;
        private readonly IPermissionService _permissionService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly INotificationService _notificationService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;
        private readonly IWebHelper _webHelper;
        private readonly IWorkContext _workContext;
        private readonly ShoppingCartSettings _shoppingCartSettings;

        #endregion

        #region Ctor

        public PaymentPhonePeController(IGenericAttributeService genericAttributeService,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            IPaymentPluginManager paymentPluginManager,
            IPermissionService permissionService,
            ILocalizationService localizationService,
            ILogger logger,
            INotificationService notificationService,
            ISettingService settingService,
            IStoreContext storeContext,
            IWebHelper webHelper,
            IWorkContext workContext,
            ShoppingCartSettings shoppingCartSettings)
        {
            _genericAttributeService = genericAttributeService;
            _orderProcessingService = orderProcessingService;
            _orderService = orderService;
            _paymentPluginManager = paymentPluginManager;
            _permissionService = permissionService;
            _localizationService = localizationService;
            _logger = logger;
            _notificationService = notificationService;
            _settingService = settingService;
            _storeContext = storeContext;
            _webHelper = webHelper;
            _workContext = workContext;
            _shoppingCartSettings = shoppingCartSettings;
        }

        #endregion

        #region Utilities

        protected virtual void ProcessRecurringPayment(string invoiceId, PaymentStatus newPaymentStatus, string transactionId, string ipnInfo)
        {
            Guid orderNumberGuid;

            try
            {
                orderNumberGuid = new Guid(invoiceId);
            }
            catch
            {
                orderNumberGuid = Guid.Empty;
            }

            var order = _orderService.GetOrderByGuid(orderNumberGuid);
            if (order == null)
            {
                _logger.Error("PayPal IPN. Order is not found", new NopException(ipnInfo));
                return;
            }

            var recurringPayments = _orderService.SearchRecurringPayments(initialOrderId: order.Id);

            foreach (var rp in recurringPayments)
            {
                switch (newPaymentStatus)
                {
                    case PaymentStatus.Authorized:
                    case PaymentStatus.Paid:
                        {
                            var recurringPaymentHistory = rp.RecurringPaymentHistory;
                            if (!recurringPaymentHistory.Any())
                            {
                                //first payment
                                var rph = new RecurringPaymentHistory
                                {
                                    RecurringPaymentId = rp.Id,
                                    OrderId = order.Id,
                                    CreatedOnUtc = DateTime.UtcNow
                                };
                                rp.RecurringPaymentHistory.Add(rph);
                                _orderService.UpdateRecurringPayment(rp);
                            }
                            else
                            {
                                //next payments
                                var processPaymentResult = new ProcessPaymentResult
                                {
                                    NewPaymentStatus = newPaymentStatus
                                };
                                if (newPaymentStatus == PaymentStatus.Authorized)
                                    processPaymentResult.AuthorizationTransactionId = transactionId;
                                else
                                    processPaymentResult.CaptureTransactionId = transactionId;

                                _orderProcessingService.ProcessNextRecurringPayment(rp,
                                    processPaymentResult);
                            }
                        }

                        break;
                    case PaymentStatus.Voided:
                        //failed payment
                        var failedPaymentResult = new ProcessPaymentResult
                        {
                            Errors = new[] { $"PayPal IPN. Recurring payment is {nameof(PaymentStatus.Voided).ToLower()} ." },
                            RecurringPaymentFailed = true
                        };
                        _orderProcessingService.ProcessNextRecurringPayment(rp, failedPaymentResult);
                        break;
                }
            }

            //OrderService.InsertOrderNote(newOrder.OrderId, sb.ToString(), DateTime.UtcNow);
            _logger.Information("PayPal IPN. Recurring info", new NopException(ipnInfo));
        }

        protected virtual void ProcessPayment(string orderNumber, string ipnInfo, PaymentStatus newPaymentStatus, decimal mcGross, string transactionId)
        {
            Guid orderNumberGuid;

            try
            {
                orderNumberGuid = new Guid(orderNumber);
            }
            catch
            {
                orderNumberGuid = Guid.Empty;
            }

            var order = _orderService.GetOrderByGuid(orderNumberGuid);

            if (order == null)
            {
                _logger.Error("PayPal IPN. Order is not found", new NopException(ipnInfo));
                return;
            }

            //order note
            order.OrderNotes.Add(new OrderNote
            {
                Note = ipnInfo,
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });

            _orderService.UpdateOrder(order);

            //validate order total
            if ((newPaymentStatus == PaymentStatus.Authorized || newPaymentStatus == PaymentStatus.Paid) && !Math.Round(mcGross, 2).Equals(Math.Round(order.OrderTotal, 2)))
            {
                var errorStr = $"PayPal IPN. Returned order total {mcGross} doesn't equal order total {order.OrderTotal}. Order# {order.Id}.";
                //log
                _logger.Error(errorStr);
                //order note
                order.OrderNotes.Add(new OrderNote
                {
                    Note = errorStr,
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });
                _orderService.UpdateOrder(order);

                return;
            }

            switch (newPaymentStatus)
            {
                case PaymentStatus.Authorized:
                    if (_orderProcessingService.CanMarkOrderAsAuthorized(order))
                        _orderProcessingService.MarkAsAuthorized(order);
                    break;
                case PaymentStatus.Paid:
                    if (_orderProcessingService.CanMarkOrderAsPaid(order))
                    {
                        order.AuthorizationTransactionId = transactionId;
                        _orderService.UpdateOrder(order);

                        _orderProcessingService.MarkOrderAsPaid(order);
                    }

                    break;
                case PaymentStatus.Refunded:
                    var totalToRefund = Math.Abs(mcGross);
                    if (totalToRefund > 0 && Math.Round(totalToRefund, 2).Equals(Math.Round(order.OrderTotal, 2)))
                    {
                        //refund
                        if (_orderProcessingService.CanRefundOffline(order))
                            _orderProcessingService.RefundOffline(order);
                    }
                    else
                    {
                        //partial refund
                        if (_orderProcessingService.CanPartiallyRefundOffline(order, totalToRefund))
                            _orderProcessingService.PartiallyRefundOffline(order, totalToRefund);
                    }

                    break;
                case PaymentStatus.Voided:
                    if (_orderProcessingService.CanVoidOffline(order))
                        _orderProcessingService.VoidOffline(order);

                    break;
            }
        }

        #endregion

        #region Methods

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = _storeContext.ActiveStoreScopeConfiguration;
            var phonePePaymentSettings = _settingService.LoadSetting<PhonePePaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                UseSandbox = phonePePaymentSettings.UseSandbox,
                SandboxURL = phonePePaymentSettings.SandboxURL,
                ProductionURL = phonePePaymentSettings.ProductionURL,
                MerchantId = phonePePaymentSettings.MerchantId,
                Salt = phonePePaymentSettings.Salt,
                ActiveStoreScopeConfiguration = storeScope
            };

            if (storeScope <= 0)
                return View("~/Plugins/Payments.PhonePe/Views/Configure.cshtml", model);

            model.UseSandbox_OverrideForStore = _settingService.SettingExists(phonePePaymentSettings, x => x.UseSandbox, storeScope);
            model.SandboxURL_OverrideForStore = _settingService.SettingExists(phonePePaymentSettings, x => x.SandboxURL, storeScope);
            model.ProductionURL_OverrideForStore = _settingService.SettingExists(phonePePaymentSettings, x => x.ProductionURL, storeScope);
            model.MerchantId_OverrideForStore = _settingService.SettingExists(phonePePaymentSettings, x => x.MerchantId, storeScope);
            model.Salt_OverrideForStore = _settingService.SettingExists(phonePePaymentSettings, x => x.Salt, storeScope);

            return View("~/Plugins/Payments.PhonePe/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [AdminAntiForgery]
        [Area(AreaNames.Admin)]
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return Configure();

            //load settings for a chosen store scope
            var storeScope = _storeContext.ActiveStoreScopeConfiguration;
            var phonePePaymentSettings = _settingService.LoadSetting<PhonePePaymentSettings>(storeScope);

            //save settings
            phonePePaymentSettings.UseSandbox = model.UseSandbox;
            phonePePaymentSettings.SandboxURL = model.SandboxURL;
            phonePePaymentSettings.ProductionURL = model.ProductionURL;
            phonePePaymentSettings.MerchantId = model.MerchantId;
            phonePePaymentSettings.Salt = model.Salt;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            _settingService.SaveSettingOverridablePerStore(phonePePaymentSettings, x => x.UseSandbox, model.UseSandbox_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(phonePePaymentSettings, x => x.SandboxURL, model.SandboxURL_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(phonePePaymentSettings, x => x.ProductionURL, model.ProductionURL_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(phonePePaymentSettings, x => x.MerchantId, model.MerchantId_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(phonePePaymentSettings, x => x.Salt, model.Salt_OverrideForStore, storeScope, false);

            //now clear settings cache
            _settingService.ClearCache();

            _notificationService.SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }

        //action displaying notification (warning) to a store owner about inaccurate PayPal rounding
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult RoundingWarning(bool passProductNamesAndTotals)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //prices and total aren't rounded, so display warning
            if (passProductNamesAndTotals && !_shoppingCartSettings.RoundPricesDuringCalculation)
                return Json(new { Result = _localizationService.GetResource("Plugins.Payments.PhonePe.RoundingWarning") });

            return Json(new { Result = string.Empty });
        }

        public class PostResponse
        {
            public string MerchantId { get; set; }
            public string TransactionId { get; set; }
        }


        [IgnoreAntiforgeryToken]
        public IActionResult HandlePostBack([FromForm] PostResponse postResponse)
        {
            var OrderId = postResponse.TransactionId;

            if (!(_paymentPluginManager.LoadPluginBySystemName("Payments.PhonePe") is PhonePePaymentProcessor processor) || !_paymentPluginManager.IsPluginActive(processor))
                throw new NopException("PhonePe module cannot be loaded");


            if (processor.GetPdtDetails(OrderId, out PaymentResponse paymentResponse))
            {
                var order = _orderService.GetOrderById(Convert.ToInt32(paymentResponse.Data.MerchantTransactionId));

                if (order == null)
                    return RedirectToAction("Index", "Home", new { area = string.Empty });


                var sb = new StringBuilder();
                sb.AppendLine("PhonePe:");
                sb.AppendLine("Amount: " + paymentResponse.Data.Amount / 100);
                sb.AppendLine("Payment status: " + paymentResponse.Code);
                sb.AppendLine("Pending message: " + paymentResponse.Message);
                sb.AppendLine("txn_id: " + paymentResponse.Data.PaymentInstrument.BankTransactionId);
                sb.AppendLine("payment_type: " + paymentResponse.Data.PaymentInstrument.Type);
                sb.AppendLine("TransactionId: " + paymentResponse.Data.TransactionId);
                sb.AppendLine("Type: " + paymentResponse.Data.PaymentInstrument.Type);

                var newPaymentStatus = PhonePeHelper.GetPaymentStatus(paymentResponse.Code, string.Empty);
                //sb.AppendLine("New payment status: " + "paid");


                order.OrderNotes.Add(new OrderNote
                {
                    OrderId = order.Id,
                    Note = sb.ToString(),
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });
                _orderService.UpdateOrder(order);
                //validate order total
                var orderTotalSentToPhonePe = _genericAttributeService.GetAttribute<decimal?>(order, PhonePeHelper.OrderTotalSentToPhonePe);

                //clear attribute
                if (orderTotalSentToPhonePe.HasValue)
                    _genericAttributeService.SaveAttribute<decimal?>(order, PhonePeHelper.OrderTotalSentToPhonePe, null);

                if (newPaymentStatus != PaymentStatus.Paid)
                    return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });

                if (!_orderProcessingService.CanMarkOrderAsPaid(order))
                    return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });

                //mark order as paid
                order.AuthorizationTransactionId = paymentResponse.Data.PaymentInstrument.BankTransactionId;
                _orderService.UpdateOrder(order);
                _orderProcessingService.MarkOrderAsPaid(order);

                return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
            }
            else
            {
                var order = _orderService.GetOrderById(Convert.ToInt32(paymentResponse.Data.MerchantTransactionId));
                if (order == null)
                    return RedirectToAction("Index", "Home", new { area = string.Empty });


                var sb = new StringBuilder();
                sb.AppendLine("PhonePe:");
                sb.AppendLine("Amount: " + paymentResponse.Data.Amount / 100);
                sb.AppendLine("Payment status: " + paymentResponse.Code);
                sb.AppendLine("Pending message: " + paymentResponse.Message);
                sb.AppendLine("txn_id: " + paymentResponse.Data.PaymentInstrument.BankTransactionId);
                sb.AppendLine("payment_type: " + paymentResponse.Data.PaymentInstrument.Type);
                sb.AppendLine("TransactionId: " + paymentResponse.Data.TransactionId);
                sb.AppendLine("Type: " + paymentResponse.Data.PaymentInstrument.Type);
                //order note
                
                order.OrderNotes.Add(new OrderNote
                {
                    OrderId = order.Id,
                    Note = "PhonePe payment failed. - " + sb.ToString(),
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });
                _orderService.UpdateOrder(order);

                return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
            }
        }

        public IActionResult CancelOrder()
        {
            var order = _orderService.SearchOrders(_storeContext.CurrentStore.Id,
                customerId: _workContext.CurrentCustomer.Id, pageSize: 1).FirstOrDefault();

            if (order != null)
                return RedirectToRoute("OrderDetails", new { orderId = order.Id });

            return RedirectToRoute("Homepage");
        }

        #endregion
    }
}