using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Models.Refunds;

namespace Payment.DomainService.Services;

public sealed class PaymentRefundRequestFactory :
    IPaymentRefundRequestFactory
{
    public ProviderRefundRequest Create(
        PaymentDetail payment,
        PaymentRefund refund,
        long minorUnits)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(refund);

        return new ProviderRefundRequest
        {
            MerchantAccount =
                refund.ProviderMerchantAccount,
            Amount = new ProviderAmount
            {
                Value = minorUnits,
                Currency = refund.CurrencyCode
            },
            Reference = refund.ProviderReference,
            OrganizationId = payment.OrganizationId
        };
    }

    public ProviderReversalRequest CreateReversal(
        PaymentRefund refund) =>
        new()
        {
            MerchantAccount = refund.ProviderMerchantAccount,
            Reference = refund.ProviderReference
        };
}
