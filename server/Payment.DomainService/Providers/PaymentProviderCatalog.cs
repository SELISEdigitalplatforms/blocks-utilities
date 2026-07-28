using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers;

/// <summary>
/// Immutable catalog of the providers this build supports. Registering a new provider is a
/// single addition to <see cref="RegisteredNames"/>; the names themselves stay defined on
/// <see cref="PaymentConstants"/> so no literal is spelled twice.
/// </summary>
public sealed class PaymentProviderCatalog : IPaymentProviderCatalog
{
    private static readonly string[] RegisteredNames =
    [
        PaymentConstants.AdyenOnlineProvider,
        PaymentConstants.StripeProvider
    ];

    private readonly HashSet<string> _registeredProviderNames =
        new(RegisteredNames, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> RegisteredProviderNames => RegisteredNames;

    public bool IsRegistered(string? providerName) =>
        !string.IsNullOrWhiteSpace(providerName) &&
        _registeredProviderNames.Contains(providerName);
}
