using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Providers;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Routes secret resolution to the provider that owns the secret shape. Fails closed when no
/// provider claims the configuration, so an unrecognised provider is never admitted to the
/// cache with empty credentials.
/// </summary>
public sealed class PaymentProviderSecretHydrator : IPaymentProviderSecretHydrator
{
    private readonly IReadOnlyCollection<IProviderSecretHydrator> _hydrators;
    private readonly ILogger<PaymentProviderSecretHydrator> _logger;

    public PaymentProviderSecretHydrator(
        IEnumerable<IProviderSecretHydrator> hydrators,
        ILogger<PaymentProviderSecretHydrator> logger)
    {
        _hydrators = hydrators.ToArray();
        _logger = logger;
    }

    public Task<bool> HydrateAsync(
        PaymentProvider provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var hydrator = _hydrators.FirstOrDefault(candidate =>
            candidate.Supports(provider.ProviderName));

        if (hydrator != null)
        {
            return hydrator.HydrateAsync(provider, cancellationToken);
        }

        _logger.LogError(
            "Payment provider secrets could not be resolved Provider={Provider} TenantHash={TenantHash} Reason=no_secret_hydrator_registered",
            PaymentLogValue.Label(provider.ProviderName),
            PaymentLogValue.Hash(provider.TenantId));

        return Task.FromResult(false);
    }
}
