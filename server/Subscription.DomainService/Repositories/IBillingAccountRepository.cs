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
    /// Records the provider's identifiers once the first charge has confirmed them.
    /// </summary>
    /// <remarks>
    /// Only fills a blank customer id, never overwrites one. A second charge naming a different
    /// customer means something is wrong, and quietly adopting it would strand every saved card
    /// on the first.
    /// </remarks>
    Task<bool> TrySetProviderCustomerAsync(
        string tenantId,
        string billingAccountId,
        string providerCustomerId,
        string? defaultPaymentMethodId,
        CancellationToken cancellationToken);
}
