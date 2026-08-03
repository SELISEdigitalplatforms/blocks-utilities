using Payment.DomainService.Entities;
using Payment.DomainService.Models.Captures;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Services;

public sealed class PaymentCaptureRequestFactory :
    IPaymentCaptureRequestFactory
{
    public ProviderCaptureRequest Create(
        PaymentCapture capture,
        long minorUnits) =>
        new()
        {
            MerchantAccount = capture.ProviderMerchantAccount,
            Amount = new ProviderAmount
            {
                Value = minorUnits,
                Currency = capture.CurrencyCode
            },
            Reference = capture.ProviderReference
        };
}
