using Blocks.Genesis;
using MongoDB.Driver;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// Resolves a tenant's collections.
/// </summary>
/// <remarks>
/// The tenant selects the database, so it travels as an argument on every repository call
/// rather than being read from ambient context. That keeps repositories singleton and usable
/// from background work, which has no request to read a tenant from.
/// </remarks>
internal static class SubscriptionCollections
{
    public const string Plans = "SubscriptionPlans";
    public const string Prices = "SubscriptionPrices";
    public const string Discounts = "SubscriptionDiscounts";
    public const string BillingAccounts = "SubscriptionBillingAccounts";
    public const string Subscriptions = "Subscriptions";
    public const string UsageRecords = "SubscriptionUsageRecords";
    public const string UsageCounters = "SubscriptionUsageCounters";
    public const string PaymentLinks = "SubscriptionPaymentLinks";
    public const string UsageInvoices = "SubscriptionUsageInvoices";
    public const string AuditEvents = "SubscriptionAuditEvents";
    public const string SimulationRuns = "SubscriptionSimulationRuns";

    public static IMongoCollection<TDocument> Of<TDocument>(
        IDbContextProvider dbContextProvider,
        string tenantId,
        string collectionName) =>
        dbContextProvider
            .GetDatabase(RequireTenant(tenantId))
            .GetCollection<TDocument>(collectionName);

    private static string RequireTenant(string tenantId) =>
        !string.IsNullOrWhiteSpace(tenantId)
            ? tenantId
            : throw new InvalidOperationException(
                "A tenant id is required for subscription persistence.");
}
