using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;

namespace Payment.DomainService.Providers;

/// <summary>Builds one provider's checkout request from a payment about to be initiated.</summary>
public interface IProviderInitiationRequestFactory
{
    bool Supports(string providerName);

    /// <param name="providerPayerReference">
    /// The provider's own identifier for this shopper, where one is already known from a card
    /// they saved earlier. Null the first time they pay. Providers that address the shopper by
    /// <paramref name="shopperReference"/> alone ignore it.
    /// </param>
    ProviderInitiationRequest Create(
        MakePaymentRequest request,
        PaymentExecutionContext context,
        PaymentDetail payment,
        PaymentProvider provider,
        string returnUrl,
        string providerReference,
        string shopperReference,
        string? providerPayerReference,
        bool includeStoredPaymentMethods,
        long minorUnits);
}
