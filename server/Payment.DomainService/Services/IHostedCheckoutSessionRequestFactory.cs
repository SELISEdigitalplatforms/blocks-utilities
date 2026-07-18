using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Requests;

namespace Payment.DomainService.Services;

public interface IHostedCheckoutSessionRequestFactory
{
    HostedCheckoutSessionRequest Create(
        MakePaymentRequest request,
        PaymentExecutionContext context,
        PaymentDetail payment,
        PaymentProvider provider,
        string returnUrl,
        string providerReference,
        string shopperReference,
        bool includeStoredPaymentMethods,
        long minorUnits);
}
