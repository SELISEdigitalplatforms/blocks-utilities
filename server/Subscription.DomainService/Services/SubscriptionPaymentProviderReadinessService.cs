using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
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
/// Resolution itself is <see cref="IPaymentRepository.GetProviderAsync"/> -- the exact method a
/// real charge resolves its provider through, enabled-filtering and organization/tenant/console
/// scope fallback included (see <see cref="PaymentProviderScopeChain"/>). This is deliberate and
/// load-bearing: a provider readiness declares <see cref="SubscriptionPaymentProviderReadiness.Ready"/>
/// for is provably the same configuration a charge would actually be routed through, because both
/// ask the same repository the same question. An earlier version of this service reimplemented
/// scope resolution itself, using <see cref="IPaymentRepository.GetProvidersAsync"/> (which does
/// not filter by <c>IsEnabled</c>) and picking only the first candidate per scope -- so an
/// organization-specific configuration that happened to be disabled reported the whole chain
/// Disabled, even when a broader tenant- or console-level configuration was enabled and would
/// have served the charge. That divergence is exactly what a readiness check must never have from
/// the payment module's own resolution, so this now defers to it entirely for the Ready decision.
/// <para>
/// <see cref="IPaymentRepository.GetProvidersAsync"/> is still used, but only after
/// <see cref="IPaymentRepository.GetProviderAsync"/> has already found nothing -- purely to tell a
/// caller *why* nothing matched (no configuration exists at all, versus one exists somewhere in
/// the scope chain but is switched off). That distinction is diagnostic only: it never overrides,
/// and cannot disagree with, the enabled/fallback decision <see cref="IPaymentRepository.GetProviderAsync"/>
/// already made.
/// </para>
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

    public async Task<SubscriptionPaymentProviderReadinessResult> CheckAsync(
        string tenantId,
        string? organizationId,
        string providerName,
        CancellationToken cancellationToken)
    {
        if (!_catalog.IsRegistered(providerName))
        {
            return SubscriptionPaymentProviderReadinessResult.NotReady(
                SubscriptionPaymentProviderReadiness.Unsupported);
        }

        // The exact lookup a charge resolves its provider through -- enabled-filtering and the
        // organization -> tenant -> console scope chain both applied inside the repository call
        // itself, not reimplemented here. See the type's remarks.
        var candidate = await _providers.GetProviderAsync(
            tenantId, organizationId, providerName, cancellationToken);

        if (candidate is null)
        {
            return SubscriptionPaymentProviderReadinessResult.NotReady(
                await DiagnoseAbsenceAsync(tenantId, organizationId, providerName, cancellationToken));
        }

        if (string.IsNullOrWhiteSpace(candidate.ApiBaseUrl) ||
            string.IsNullOrWhiteSpace(candidate.MerchantId))
        {
            return new SubscriptionPaymentProviderReadinessResult(
                SubscriptionPaymentProviderReadiness.Misconfigured, candidate);
        }

        var hydrated = await _secrets.HydrateAsync(candidate, cancellationToken);

        if (!hydrated || !HasRequiredSecrets(candidate))
        {
            return new SubscriptionPaymentProviderReadinessResult(
                SubscriptionPaymentProviderReadiness.CredentialsUnavailable, candidate);
        }

        return new SubscriptionPaymentProviderReadinessResult(
            SubscriptionPaymentProviderReadiness.Ready, candidate);
    }

    /// <summary>
    /// Distinguishes "nothing configured anywhere in the scope chain" from "something is
    /// configured, but it's switched off" -- purely for the error a caller sees. Never
    /// participates in whether a charge would succeed; that question was already answered by
    /// <see cref="IPaymentRepository.GetProviderAsync"/> returning null.
    /// </summary>
    private async Task<SubscriptionPaymentProviderReadiness> DiagnoseAbsenceAsync(
        string tenantId,
        string? organizationId,
        string providerName,
        CancellationToken cancellationToken)
    {
        var all = await _providers.GetProvidersAsync(tenantId, cancellationToken);

        var anyDisabledCandidateInScope = PaymentProviderScopeChain
            .Candidates(organizationId, _paymentOptions.Value)
            .Any(scopeOrganizationId => all.Any(provider =>
                string.Equals(provider.ProviderName, providerName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(provider.OrganizationId, scopeOrganizationId, StringComparison.Ordinal)));

        return anyDisabledCandidateInScope
            ? SubscriptionPaymentProviderReadiness.Disabled
            : SubscriptionPaymentProviderReadiness.NotConfigured;
    }

    /// <summary>
    /// The secrets a charge cannot be trusted without, per provider -- what the provider itself
    /// requires to authenticate a call and to verify the webhook that alone can confirm money
    /// moved, not merely what happens to be populated.
    /// </summary>
    private static bool HasRequiredSecrets(PaymentProvider provider) =>
        string.Equals(provider.ProviderName, PaymentConstants.StripeProvider, StringComparison.OrdinalIgnoreCase)
            ? !string.IsNullOrWhiteSpace(provider.StandardWebhookHmacKey)
            : !string.IsNullOrWhiteSpace(provider.ApiKey) &&
              !string.IsNullOrWhiteSpace(provider.StandardWebhookHmacKey) &&
              !string.IsNullOrWhiteSpace(provider.TokenWebhookHmacKey);
}
