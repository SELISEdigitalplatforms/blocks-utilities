namespace Payment.DomainService.Providers;

/// <summary>
/// The set of payment providers this service is built to execute. Acts as the single
/// admission gate for provider names arriving on inbound requests.
/// </summary>
public interface IPaymentProviderCatalog
{
    /// <summary>Canonical names of every registered provider.</summary>
    IReadOnlyCollection<string> RegisteredProviderNames { get; }

    /// <summary>
    /// Whether <paramref name="providerName"/> names a registered provider. Comparison
    /// ignores case because provider names arrive from callers and stored documents alike.
    /// </summary>
    bool IsRegistered(string? providerName);
}
