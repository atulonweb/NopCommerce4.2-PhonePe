using System;
using System.Collections.Generic;
using System.Text;

namespace Nop.Plugin.Payments.PhonePe.Models
{
    public class PaymentResponse
    {
        public bool Success { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public PaymentData Data { get; set; }
    }

    public class PaymentData
    {
        public string MerchantId { get; set; }
        public string MerchantTransactionId { get; set; }
        public string TransactionId { get; set; }
        public int Amount { get; set; }
        public string State { get; set; }
        public string ResponseCode { get; set; }
        public PaymentInstrument PaymentInstrument { get; set; }
    }

    public class PaymentInstrument
    {
        public string Type { get; set; }
        public string CardType { get; set; }
        public string PgTransactionId { get; set; }
        public string BankTransactionId { get; set; }
        public string PgAuthorizationCode { get; set; }
        public string Arn { get; set; }
        public string BankId { get; set; }
        public string Brn { get; set; }
    }
}
