using Subscription.DomainService.Enums;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public interface IBillingAccountRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the organization's account with this provider, creating it if there is none, and
    /// bringing its contact details up to date either way.
    /// </summary>
    /// <remarks>
    /// Named for the reconciling half because leaving it out was a bug. This used to return an
    /// existing account untouched, so an organization that fixed a blank or wrong billing profile
    /// and subscribed again kept the old contact on the account — and renewal and usage-threshold
    /// mail went on going nowhere, or to the previous address, with the corrected profile sitting
    /// right there. An account is one per organization and provider and outlives every subscription
    /// on it, so "create it correctly" was never enough.
    /// <para>
    /// <see cref="BillingAccount.BillingEmail"/> and <see cref="BillingAccount.BillingName"/> on the
    /// argument are the values to reconcile <em>to</em>, already resolved by the caller: an explicit
    /// request value takes precedence over the organization's billing profile, per field. A null
    /// leaves whatever is stored alone rather than blanking it, so a caller that knows only an
    /// address cannot erase a name.
    /// </para>
    /// <para>
    /// A profile-derived value does overwrite a stored one. That is the point — a stale address is
    /// the reported failure — and it means an integration that sets a contact once and then
    /// subscribes again without naming it will see the profile's value take over. Send the value on
    /// every request if you keep your own record of the customer.
    /// </para>
    /// <para>
    /// One round trip, and safe under concurrency without a read-then-write window: an upsert keyed
    /// on the unique index, whose creation-only fields are written under <c>$setOnInsert</c>. Two
    /// concurrent signups converge on one document, and both were reconciling to the same values.
    /// </para>
    /// </remarks>
    Task<BillingAccount> GetOrCreateAndReconcileAsync(
        BillingAccount account,
        CancellationToken cancellationToken);

    Task<BillingAccount?> GetAsync(
        string tenantId,
        string billingAccountId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the provider's identifiers once a charge has confirmed them.
    /// </summary>
    /// <remarks>
    /// Adopts a customer id that differs from the one already stored, and reports having done
    /// so through <see cref="SetProviderCustomerOutcome"/> so the caller can say it out loud.
    /// <para>
    /// This used to refuse, on the reasoning that a second charge naming a different customer
    /// is a sign something is wrong and adopting it would strand the cards saved on the first.
    /// The first half is true; the conclusion was backwards. Refusing does not keep those cards
    /// reachable — it leaves the account naming a customer that no later payment writes to,
    /// while the card that was actually charged sits on the new one. A renewal then presents a
    /// card the shopper has removed, a month after the divergence, with nothing logged at the
    /// time. Following the money is the recoverable choice; noticing in silence was not.
    /// </para>
    /// </remarks>
    /// <param name="providerOrganizationId">
    /// The organization whose merchant configuration took the card, which is what later charges
    /// resolve the provider under — see <see cref="Entities.BillingAccount.ProviderOrganizationId"/>.
    /// </param>
    Task<SetProviderCustomerOutcome> TrySetProviderCustomerAsync(
        string tenantId,
        string billingAccountId,
        string providerCustomerId,
        string? defaultPaymentMethodId,
        string? providerOrganizationId,
        CancellationToken cancellationToken);
}
