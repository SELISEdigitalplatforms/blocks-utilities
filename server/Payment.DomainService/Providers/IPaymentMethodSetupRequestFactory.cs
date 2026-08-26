using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Requests;

namespace Payment.DomainService.Providers;

/// <summary>
/// Builds one provider's card-collection request, for a session that takes no money.
/// </summary>
/// <remarks>
/// Separate from <see cref="IProviderInitiationRequestFactory"/> rather than a flag on it. The
/// two requests share a transport and nothing else: one has line items, a capture mode and an
/// amount, and the other exists precisely because there are none. A provider that cannot
/// collect a card without charging it simply has no factory here.
/// </remarks>
public interface IPaymentMethodSetupRequestFactory
{
    bool Supports(string providerName);

    /// <param name="providerPayerReference">
    /// The provider's own identifier for this shopper, where a card they saved earlier already
    /// established one. Null the first time, and the provider then mints its own.
    /// </param>
    ProviderInitiationRequest Create(
        CreatePaymentMethodSetupRequest request,
        PaymentDetail payment,
        PaymentProvider provider,
        string returnUrl,
        string providerReference,
        string shopperReference,
        string? providerPayerReference);
}
