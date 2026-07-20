using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Models.Refunds;

namespace Payment.DomainService.Services;

public sealed class PaymentRefundRequestFactory :
    IPaymentRefundRequestFactory
{
    public ProviderRefundRequest Create(
        PaymentRefund refund,
        long minorUnits) =>
        new()
        {
            MerchantAccount =
                refund.ProviderMerchantAccount,
            Amount = new ProviderAmount
            {
                Value = minorUnits,
                Currency = refund.CurrencyCode
            },
            Reference = refund.ProviderReference
        };

    public ProviderReversalRequest CreateReversal(
        PaymentRefund refund) =>
        new()
        {
            MerchantAccount = refund.ProviderMerchantAccount,
            Reference = refund.ProviderReference
        };
}
