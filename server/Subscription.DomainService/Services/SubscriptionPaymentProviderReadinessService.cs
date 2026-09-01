using Microsoft.Extensions.Options;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Services;

/// <summary>
/// Answers "is this provider ready to take money for this tenant right now", against the same
/// configuration and secret material the payment module itself resolves a charge through.
/// </summary>
/// <remarks>
/// Deliberately reads <see cref="IPaymentRepository.GetProvidersAsync"/> rather than
/// <see cref="IPaymentRepository.GetProviderAsync"/>: that lookup already filters to enabled
/// configurations, which would make <see cref="SubscriptionPaymentProviderReadiness.NotConfigured"/>
/// and <see cref="SubscriptionPaymentProviderReadiness.Disabled"/> indistinguishable from each
/// other. The scope-chain order (organization, then tenant, then console-as-tenant-wide) is
/// reproduced here rather than reused from <see cref="PaymentProviderScopeChain"/>'s caller,
/// because that helper only orders candidate organization ids -- the enabled-filtering happens
/// in the query itself, which is exactly what this has to not do.
/// </remarks>
public sealed class SubscriptionPaymentProviderReadinessService : ISubscriptionPaymentProviderReadinessService
{
    private readonly IPaymentProviderCatalog _catalog;
    private readonly IPaymentRepository _providers;
    private readonly IPaymentProviderSecretHydrator _secrets;
    private readonly IOptions<PaymentOptions> _paymentOptions;

    public SubscriptionPaymentProviderReadinessService(
        IPaymentProviderCatalog catalog,
        IPaymentRepository providers,
        IPaymentProviderSecretHydrator secrets,
        IOptions<PaymentOptions> paymentOptions)
    {
        _catalog = catalog;
        _providers = providers;
        _secrets = secrets;
        _paymentOptions = paymentOptions;
    }

    public async Task<SubscriptionPaymentProviderReadiness> CheckAsync(
        string tenantId,
        string? organizationId,
        string providerName,
        CancellationToken cancellationToken)
    {
        if (!_catalog.IsRegistered(providerName))
        {
            return SubscriptionPaymentProviderReadiness.Unsupported;
        }

        var all = await _providers.GetProvidersAsync(tenantId, cancellationToken);

        var candidate = PaymentProviderScopeChain
            .Candidates(organizationId, _paymentOptions.Value)
            .Select(scopeOrganizationId => all.FirstOrDefault(provider =>
                string.Equals(provider.ProviderName, providerName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(provider.OrganizationId, scopeOrganizationId, StringComparison.Ordinal)))
            .FirstOrDefault(provider => provider is not null);

        if (candidate is null)
        {
            return SubscriptionPaymentProviderReadiness.NotConfigured;
        }

        if (!candidate.IsEnabled)
        {
            return SubscriptionPaymentProviderReadiness.Disabled;
        }

        if (string.IsNullOrWhiteSpace(candidate.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(candidate.MerchantId))
        {
            return SubscriptionPaymentProviderReadiness.Misconfigured;
        }

        var hydrated = await _secrets.HydrateAsync(candidate, cancellationToken);

        if (!hydrated || !HasRequiredSecrets(candidate))
        {
            return SubscriptionPaymentProviderReadiness.CredentialsUnavailable;
        }

        return SubscriptionPaymentProviderReadiness.Ready;
    }

    /// <summary>
    /// The secrets a charge cannot be trusted without, per provider -- what the provider itself
    /// requires to authenticate a call and to verify the webhook that alone can confirm money
    /// moved, not merely what happens to be populated.
    /// </summary>
    private static bool HasRequiredSecrets(Payment.DomainService.Entities.PaymentProvider provider) =>
        string.Equals(provider.ProviderName, PaymentConstants.StripeProvider, StringComparison.OrdinalIgnoreCase)
            ? !string.IsNullOrWhiteSpace(provider.StandardWebhookHmacKey)
            : !string.IsNullOrWhiteSpace(provider.ApiKey) &&
              !string.IsNullOrWhiteSpace(provider.StandardWebhookHmacKey) &&
              !string.IsNullOrWhiteSpace(provider.TokenWebhookHmacKey);
}
