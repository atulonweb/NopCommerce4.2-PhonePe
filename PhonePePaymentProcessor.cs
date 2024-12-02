using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Plugin.Payments.PhonePe.Services;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Tax;
using Newtonsoft.Json;
using System.Text;
using System.Net.Http;
using Nop.Plugin.Payments.PhonePe.Models;

namespace Nop.Plugin.Payments.PhonePe
{
    /// <summary>
    /// PhonePe payment processor
    /// </summary>
    public class PhonePePaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly ICheckoutAttributeParser _checkoutAttributeParser;
        private readonly ICurrencyService _currencyService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILocalizationService _localizationService;
        private readonly IPaymentService _paymentService;
        private readonly ISettingService _settingService;
        private readonly ITaxService _taxService;
        private readonly IWebHelper _webHelper;
        private readonly PhonePeHttpClient _phonePeHttpClient;
        private readonly PhonePePaymentSettings _phonePePaymentSettings;

        #endregion

        #region Ctor

        public PhonePePaymentProcessor(CurrencySettings currencySettings,
            ICheckoutAttributeParser checkoutAttributeParser,
            ICurrencyService currencyService,
            IGenericAttributeService genericAttributeService,
            IHttpContextAccessor httpContextAccessor,
            ILocalizationService localizationService,
            IPaymentService paymentService,
            ISettingService settingService,
            ITaxService taxService,
            IWebHelper webHelper,
            PhonePeHttpClient phonePeHttpClient,
            PhonePePaymentSettings phonePePaymentSettings)
        {
            _currencySettings = currencySettings;
            _checkoutAttributeParser = checkoutAttributeParser;
            _currencyService = currencyService;
            _genericAttributeService = genericAttributeService;
            _httpContextAccessor = httpContextAccessor;
            _localizationService = localizationService;
            _paymentService = paymentService;
            _settingService = settingService;
            _taxService = taxService;
            _webHelper = webHelper;
            _phonePeHttpClient = phonePeHttpClient;
            _phonePePaymentSettings = phonePePaymentSettings;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Gets PDT details
        /// </summary>
        /// <param name="tx">TX</param>
        /// <param name="values">Values</param>
        /// <param name="response">Response</param>
        /// <returns>Result</returns>
        public bool GetPdtDetails(string orderId, out PaymentResponse paymentResponse)
        {
            string sData = WebUtility.UrlDecode(_phonePeHttpClient.GetPdtDetailsAsync(orderId).Result);
            paymentResponse = JsonConvert.DeserializeObject<PaymentResponse>(sData);

            bool success = paymentResponse.Code.Equals("PAYMENT_SUCCESS");
            return success;
        }

        /// <summary>
        /// Verifies IPN
        /// </summary>
        /// <param name="formString">Form string</param>
        /// <param name="values">Values</param>
        /// <returns>Result</returns>
    
        /// <summary>
        /// Create common query parameters for the request
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Created query parameters</returns>
        private IDictionary<string, string> CreateQueryParameters(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //get store location
            var storeLocation = _webHelper.GetStoreLocation();

            //choosing correct order address
            var orderAddress = postProcessPaymentRequest.Order.PickupInStore
                    ? postProcessPaymentRequest.Order.PickupAddress
                    : postProcessPaymentRequest.Order.ShippingAddress;

            //create query parameters
            return new Dictionary<string, string>
            {
                //PayPal ID or an email address associated with your PayPal account
                //"business"] = _phonePePaymentSettings.BusinessEmail,

                //the character set and character encoding
                ["charset"] = "utf-8",

                //set return method to "2" (the customer redirected to the return URL by using the POST method, and all payment variables are included)
                ["rm"] = "2",

                ["bn"] = PhonePeHelper.NopCommercePartnerCode,
                ["currency_code"] = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId)?.CurrencyCode,

                //order identifier
                ["invoice"] = postProcessPaymentRequest.Order.CustomOrderNumber,
                ["custom"] = postProcessPaymentRequest.Order.OrderGuid.ToString(),

                //PDT, IPN and cancel URL
                ["return"] = $"{storeLocation}Plugins/PaymentPhonePe/PDTHandler",
                ["notify_url"] = $"{storeLocation}Plugins/PaymentPhonePe/IPNHandler",
                ["cancel_return"] = $"{storeLocation}Plugins/PaymentPhonePe/CancelOrder",

                //shipping address, if exists
                ["no_shipping"] = postProcessPaymentRequest.Order.ShippingStatus == ShippingStatus.ShippingNotRequired ? "1" : "2",
                ["address_override"] = postProcessPaymentRequest.Order.ShippingStatus == ShippingStatus.ShippingNotRequired ? "0" : "1",
                ["first_name"] = orderAddress?.FirstName,
                ["last_name"] = orderAddress?.LastName,
                ["address1"] = orderAddress?.Address1,
                ["address2"] = orderAddress?.Address2,
                ["city"] = orderAddress?.City,
                ["state"] = orderAddress?.StateProvince?.Abbreviation,
                ["country"] = orderAddress?.Country?.TwoLetterIsoCode,
                ["zip"] = orderAddress?.ZipPostalCode,
                ["email"] = orderAddress?.Email
            };
        }

          /// <summary>
        /// Add order total to the request query parameters
        /// </summary>
        /// <param name="parameters">Query parameters</param>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        private void AddOrderTotalParameters(IDictionary<string, string> parameters, PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //round order total
            var roundedOrderTotal = Math.Round(postProcessPaymentRequest.Order.OrderTotal, 2);

            parameters.Add("cmd", "_xclick");
            parameters.Add("item_name", $"Order Number {postProcessPaymentRequest.Order.CustomOrderNumber}");
            parameters.Add("amount", roundedOrderTotal.ToString("0.00", CultureInfo.InvariantCulture));

            //save order total that actually sent to PhonePe (used for PDT order total validation)
            _genericAttributeService.SaveAttribute(postProcessPaymentRequest.Order, PhonePeHelper.OrderTotalSentToPhonePe, roundedOrderTotal);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult();
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var baseUrl = _phonePePaymentSettings.UseSandbox ?
            _phonePePaymentSettings.SandboxURL :
                _phonePePaymentSettings.ProductionURL;

            //create common query parameters for the request
            var queryParameters = CreateQueryParameters(postProcessPaymentRequest);
            var tempQueryParameters = queryParameters.Where(parameter => !string.IsNullOrEmpty(parameter.Value))
            .ToDictionary(parameter => parameter.Key, parameter => parameter.Value);

            var returnURL = tempQueryParameters["return"];
            var orderId = postProcessPaymentRequest.Order.Id;
            var customerId = postProcessPaymentRequest.Order.CustomerId;
            var amount = Math.Round(postProcessPaymentRequest.Order.OrderTotal * 100);
            var merchantId = _phonePePaymentSettings.MerchantId;
            var saltKey = _phonePePaymentSettings.Salt;

            var payload = new
            {
                merchantId = merchantId,
                merchantTransactionId = Convert.ToString(orderId),
                merchantUserId = Convert.ToString(customerId),
                amount = Convert.ToString(amount),
                redirectUrl = returnURL,
                redirectMode = "POST",
                callbackUrl = returnURL,
                mobileNumber = "9999999999",
                paymentInstrument = new
                {
                    type = "PAY_PAGE"
                }
            };

            string jsonPayload = JsonConvert.SerializeObject(payload);
            // Encode payload to base64
            string encode = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonPayload));

            var stringToHash = encode + "/pg/v1/pay" + saltKey;
            var sha256 = PhonePeHelper.sha256_hash(stringToHash);
            string X_VERIFY = PhonePeHelper.SignRequest(stringToHash) + "###1";
            //ends
            var requestBody = new
            {
                X_VERIFY,
                encode,
                // Other data you want to send in the request body
            };


            var httpClient = new HttpClient();
            var uri = new Uri($"{baseUrl}/pg/v1/pay");

            // Add headers
            httpClient.DefaultRequestHeaders.Add("accept", "application/json");
            httpClient.DefaultRequestHeaders.Add("X-VERIFY", X_VERIFY);

            // Create JSON request body
            var jsonBody = $"{{\"request\":\"{encode}\"}}";
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            // Send POST request
            var response = httpClient.PostAsync(uri, content);
            response.Result.EnsureSuccessStatusCode();

            // Read and deserialize the response content
            var responseContent = response.Result.Content.ReadAsStringAsync().Result;
            var data = JsonConvert.DeserializeObject<ResponseData>(responseContent);

            //or add only an order total query parameters to the request
            AddOrderTotalParameters(queryParameters, postProcessPaymentRequest);

            //remove null values from parameters
            queryParameters = queryParameters.Where(parameter => !string.IsNullOrEmpty(parameter.Value))
                .ToDictionary(parameter => parameter.Key, parameter => parameter.Value);

            var url = QueryHelpers.AddQueryString(Convert.ToString(data.Data.InstrumentResponse.RedirectInfo.Url), queryParameters);
            //var url = Convert.ToString(data.Data.InstrumentResponse.RedirectInfo.Url);
            _httpContextAccessor.HttpContext.Response.Redirect(url);
        }


        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public bool HidePaymentMethod(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return false;
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>Additional handling fee</returns>
        public decimal GetAdditionalHandlingFee(IList<ShoppingCartItem> cart)
        {
            return 0;
            //_paymentService.CalculateAdditionalFee(cart,
            //    _PhonePePaymentSettings.AdditionalFee, _PhonePePaymentSettings.AdditionalFeePercentage);
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public CapturePaymentResult Capture(CapturePaymentRequest capturePaymentRequest)
        {
            return new CapturePaymentResult { Errors = new[] { "Capture method not supported" } };
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
        {
            return new RefundPaymentResult { Errors = new[] { "Refund method not supported" } };
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public VoidPaymentResult Void(VoidPaymentRequest voidPaymentRequest)
        {
            return new VoidPaymentResult { Errors = new[] { "Void method not supported" } };
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessRecurringPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } };
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public CancelRecurringPaymentResult CancelRecurringPayment(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            return new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } };
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public bool CanRePostProcessPayment(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            //let's ensure that at least 5 seconds passed after order is placed
            //P.S. there's no any particular reason for that. we just do it
            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5)
                return false;

            return true;
        }

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>List of validating errors</returns>
        public IList<string> ValidatePaymentForm(IFormCollection form)
        {
            return new List<string>();
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>Payment info holder</returns>
        public ProcessPaymentRequest GetPaymentInfo(IFormCollection form)
        {
            return new ProcessPaymentRequest();
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentPhonePe/Configure";
        }

        /// <summary>
        /// Gets a name of a view component for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <returns>View component name</returns>
        public string GetPublicViewComponentName()
        {
            return "PaymentPhonePe";
        }

        /// <summary>
        /// Install the plugin
        /// </summary>
        public override void Install()
        {
            //settings
            _settingService.SaveSetting(new PhonePePaymentSettings
            {
                UseSandbox = true
            });
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.Fields.RedirectionTip", "You will redirected to PhonePe UPI for payment");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.Fields.SandboxURL", "SandboxURL");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.Fields.SandboxURL", "SandboxURL");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.Fields.Sandbox.Hint", "Enter Sandbox URL provided by PhonePe.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.Fields.ProductionURL", "ProductionURL");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.Fields.ProductionURL.Hint", "Enter ProductionURL provided by PhonePe.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.Fields.MerchantId", "MerchantId");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.Fields.MerchantId.Hint", "Enter MerchantId provided by PhonePe.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.Fields.Salt", "Salt");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.Fields.Salt.Hint", "Enter Salt provided by PhonePe.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.Fields.RedirectionTip", "You will be redirected to PhonePe site to complete the order.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.Fields.UseSandbox", "Use Sandbox");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.Fields.UseSandbox.Hint", "Check to enable Sandbox (testing environment).");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.Instructions", @"
                    <p>
	                    <b>Contact PhonePe for Sandbox and Production Details</b>
	                    <br />
	                    <br />Enter details below and select you want to test with sandbox or live with production
	                    <br />
                    </p>");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.PaymentMethodDescription", "You will be redirected to PhonePe site to complete the payment");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PhonePe.RoundingWarning", "It looks like you have \"ShoppingCartSettings.RoundPricesDuringCalculation\" setting disabled. Keep in mind that this can lead to a discrepancy of the order total amount, as PhonePe only rounds to two decimals.");


            base.Install();
        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<PhonePePaymentSettings>();
            //locales
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Fields.RedirectionTip");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Fields.SandboxURL");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Fields.SandboxURL.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Fields.ProductionURL");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Fields.ProductionURL.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Fields.MerchantId");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Fields.MerchantId.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Fields.Salt");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Fields.Salt.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Fields.PDTToken");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Fields.PDTToken.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Fields.RedirectionTip");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Fields.UseSandbox");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Fields.UseSandbox.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PhonePe.Instructions");

            base.Uninstall();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture => false;

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund => false;

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund => false;

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid => false;

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType => PaymentMethodType.Redirection;

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo => false;

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public string PaymentMethodDescription => _localizationService.GetResource("Plugins.Payments.PhonePe.PaymentMethodDescription");

        #endregion
    }
}