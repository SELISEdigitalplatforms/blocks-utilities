using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

/// <summary>Indexes for the credit grant ledger and the credit purchase records.</summary>
public static class SubscriptionCreditIndexDefinitions
{
    public const string GrantBucketIndexName = "credit_grant_by_bucket";
    public const string GrantMaintenanceIndexName = "credit_grant_maintenance";
    public const string PurchaseStaleIndexName = "credit_purchase_stale";
    public const string PurchaseBySubscriptionIndexName = "credit_purchase_by_subscription";
    public const string PurchaseByPaymentIndexName = "credit_purchase_by_payment";

    /// <remarks>
    /// One index serves both reads the ledger has: the gate's search for live grants in one bucket,
    /// and the history list, which is the same prefix without the status. Expiry sits last so the
    /// gate's range on it is answered from the index.
    /// </remarks>
    public static IReadOnlyCollection<CreateIndexModel<SubscriptionCreditGrant>> CreateGrantIndexes() =>
    [
        new(
            Builders<SubscriptionCreditGrant>.IndexKeys
                .Ascending(grant => grant.TenantId)
                .Ascending(grant => grant.SubscriptionId)
                .Ascending(grant => grant.MeterKey)
                .Ascending(grant => grant.SeatNumber)
                .Ascending(grant => grant.Status)
                .Ascending(grant => grant.ExpiresAtUtc),
            new CreateIndexOptions<SubscriptionCreditGrant> { Name = GrantBucketIndexName }),
        // Backs the maintenance sweep, which asks a whole tenant for active grants that are due a
        // look rather than one subscription's bucket.
        new(
            Builders<SubscriptionCreditGrant>.IndexKeys
                .Ascending(grant => grant.TenantId)
                .Ascending(grant => grant.Status)
                .Ascending(grant => grant.LastCheckedAtUtc)
                .Ascending(grant => grant.ExpiresAtUtc),
            new CreateIndexOptions<SubscriptionCreditGrant> { Name = GrantMaintenanceIndexName })
    ];

    public static IReadOnlyCollection<CreateIndexModel<SubscriptionCreditPurchase>> CreatePurchaseIndexes() =>
    [
        // Backs the recovery sweep's search for a purchase left at Pending or Charged.
        new(
            Builders<SubscriptionCreditPurchase>.IndexKeys
                .Ascending(purchase => purchase.TenantId)
                .Ascending(purchase => purchase.Status)
                .Ascending(purchase => purchase.LastUpdatedAtUtc),
            new CreateIndexOptions<SubscriptionCreditPurchase> { Name = PurchaseStaleIndexName }),
        new(
            Builders<SubscriptionCreditPurchase>.IndexKeys
                .Ascending(purchase => purchase.TenantId)
                .Ascending(purchase => purchase.SubscriptionId)
                .Descending(purchase => purchase.CreatedAtUtc),
            new CreateIndexOptions<SubscriptionCreditPurchase> { Name = PurchaseBySubscriptionIndexName }),
        // Backs the webhook fast path, which asks of every payment that moved whether a checkout
        // purchase is waiting on it.
        new(
            Builders<SubscriptionCreditPurchase>.IndexKeys
                .Ascending(purchase => purchase.TenantId)
                .Ascending(purchase => purchase.PaymentId),
            new CreateIndexOptions<SubscriptionCreditPurchase> { Name = PurchaseByPaymentIndexName })
    ];
}
