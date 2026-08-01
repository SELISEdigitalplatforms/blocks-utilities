using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers;

/// <summary>
/// Immutable catalog of the providers this build supports. Registering a new provider is a
/// single addition to <see cref="RegisteredNames"/>; the names themselves stay defined on
/// <see cref="PaymentConstants"/> so no literal is spelled twice.
/// </summary>
public sealed class PaymentProviderCatalog : IPaymentProviderCatalog
{
    private static readonly PaymentProviderDescriptor[] Registered =
    [
        new(PaymentConstants.AdyenOnlineProvider, DefaultApiBaseUrl: null),
        new(PaymentConstants.StripeProvider, Stripe.StripeConstants.ApiBaseUrl)
    ];

    private static readonly string[] RegisteredNames =
        [.. Registered.Select(descriptor => descriptor.Name)];

    private readonly Dictionary<string, PaymentProviderDescriptor> _byName =
        Registered.ToDictionary(
            descriptor => descriptor.Name,
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> RegisteredProviderNames => RegisteredNames;

    public bool IsRegistered(string? providerName) =>
        !string.IsNullOrWhiteSpace(providerName) &&
        _byName.ContainsKey(providerName);

    public bool TryGetDescriptor(
        string? providerName,
        out PaymentProviderDescriptor descriptor)
    {
        descriptor = null!;

        return !string.IsNullOrWhiteSpace(providerName) &&
               _byName.TryGetValue(providerName, out descriptor!);
    }
}
