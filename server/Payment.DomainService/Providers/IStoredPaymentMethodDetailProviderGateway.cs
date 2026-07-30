using Payment.DomainService.Entities;

namespace Payment.DomainService.Providers;

/// <summary>
/// Reads the display details of a saved card from the provider, for providers whose
/// authorization event does not carry them.
/// </summary>
/// <remarks>
/// Adyen reports brand and last four on the authorization itself, so it has no implementation
/// and none is needed. Stripe names only the payment method, so the details are read back.
/// </remarks>
public interface IStoredPaymentMethodDetailProviderGateway
{
    bool Supports(string providerName);

    /// <summary>
    /// Returns the details, or <see langword="null"/> when they cannot be read. A failure here
    /// must not prevent the card being stored — losing the brand is a cosmetic loss, losing
    /// the card is not.
    /// </summary>
    Task<StoredPaymentMethodDetail?> GetAsync(
        PaymentProvider provider,
        string providerToken,
        CancellationToken cancellationToken);
}
