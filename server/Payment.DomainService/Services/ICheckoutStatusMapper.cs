namespace Payment.DomainService.Services;

/// <summary>
/// Translates one provider's checkout status vocabulary into the shared one used for storage
/// and for choosing the shopper's redirect.
/// </summary>
public interface ICheckoutStatusMapper
{
    bool Supports(string providerName);

    /// <summary>Reduces a provider status to the shared vocabulary.</summary>
    string Normalize(string providerStatus);

    string ToRedirectStatus(string normalizedStatus);
}
