using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace Subscription.DomainService.Services;

/// <summary>
/// The billing account a standalone credit purchase charges or opens a checkout under (spec 005).
/// </summary>
/// <remarks>
/// An organization with a subscription already has one, created at signup and holding its saved
/// card. One without a subscription has none, so this creates it the way signup does: under the
/// merchant profile's selected provider, frozen to the configuration readiness resolved, with the
/// billing profile's contact defaults. The same upsert signup uses, so an organization that buys
/// credits first and subscribes later ends up with one account, not two.
/// </remarks>
public sealed class OrganizationBillingAccountResolver
{
    private readonly IBillingAccountRepository _accounts;
    private readonly ISubscriptionMerchantProfileService? _merchants;
    private readonly ISubscriptionPaymentProviderReadinessService? _readiness;
    private readonly ISubscriptionBillingProfileGuard? _profiles;

    public OrganizationBillingAccountResolver(
        IBillingAccountRepository accounts,
        ISubscriptionMerchantProfileService? merchants = null,
        ISubscriptionPaymentProviderReadinessService? readiness = null,
        ISubscriptionBillingProfileGuard? profiles = null)
    {
        _accounts = accounts;
        _merchants = merchants;
        _readiness = readiness;
        _profiles = profiles;
    }

    public async Task<SubscriptionOperationResult<BillingAccount>> ResolveAsync(
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var providerName = _merchants is not null
            ? await _merchants.ResolveProviderNameAsync(context.TenantId, cancellationToken)
            : PaymentConstants.StripeProvider;

        if (await _accounts.FindByOrganizationAsync(
                context.TenantId, context.OrganizationId, providerName, cancellationToken) is { } existing)
        {
            return SubscriptionOperationResult<BillingAccount>.Success(existing, correlationId);
        }

        SubscriptionPaymentProviderReadinessResult? readiness = null;

        if (_readiness is not null)
        {
            readiness = await _readiness.CheckAsync(
                context.TenantId, context.OrganizationId, providerName, cancellationToken);

            if (readiness.Readiness != SubscriptionPaymentProviderReadiness.Ready)
            {
                return SubscriptionOperationResult<BillingAccount>.Failure(
                    PaymentFailureKind.Validation,
                    "subscription_payment_provider_not_ready",
                    "This tenant's selected payment provider is not ready to take a charge. An " +
                        "administrator needs to fix its configuration before credits can be bought.",
                    correlationId);
            }
        }

        var contact = _profiles is not null
            ? await _profiles.ContactDefaultsAsync(context.TenantId, context.OrganizationId, cancellationToken)
            : default;

        var account = await _accounts.GetOrCreateAndReconcileAsync(
            new BillingAccount
            {
                TenantId = context.TenantId,
                OrganizationId = context.OrganizationId,
                ProviderName = providerName,
                // Read as-is, null included, for the reason SubscriptionCreationService gives: a
                // tenant-wide configuration's null scope is a fact, not a blank to fill in.
                ProviderOrganizationId = readiness is not null
                    ? readiness.Provider?.OrganizationId
                    : context.OrganizationId,
                ProviderId = readiness?.Provider?.ItemId,
                BillingEmail = contact.Email,
                BillingName = contact.Name
            },
            cancellationToken);

        return SubscriptionOperationResult<BillingAccount>.Success(account, correlationId);
    }
}
