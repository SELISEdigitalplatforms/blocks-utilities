using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;

namespace Payment.DomainService.Providers;

/// <summary>Builds one provider's checkout request from a payment about to be initiated.</summary>
public interface IProviderInitiationRequestFactory
{
    bool Supports(string providerName);

    ProviderInitiationRequest Create(
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
