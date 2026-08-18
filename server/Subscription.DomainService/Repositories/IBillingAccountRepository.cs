using Subscription.DomainService.Enums;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public interface IBillingAccountRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the organization's account with this provider, creating it if there is none.
    /// </summary>
    /// <remarks>
    /// Idempotent by way of the unique index: two concurrent signups both attempt the insert,
    /// one loses on the duplicate key and reads what the other wrote.
    /// </remarks>
    Task<BillingAccount> GetOrCreateAsync(
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
