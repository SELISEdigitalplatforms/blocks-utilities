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
    public const string BillingProfiles = "SubscriptionBillingProfiles";

    public const string MerchantProfiles = "SubscriptionMerchantProfiles";
    public const string FinancialDocuments = "SubscriptionFinancialDocuments";

    /// <summary>
    /// One counter document per prefix and year. Its own collection rather than a field on anything,
    /// because it is the only thing here that two workers increment at the same instant.
    /// </summary>
    public const string DocumentNumbers = "SubscriptionDocumentNumbers";

    public const string DocumentCursors = "SubscriptionDocumentCursors";

    public const string UsagePeriodClosures = "SubscriptionUsagePeriodClosures";
    public const string UsagePeriodClaims = "SubscriptionUsagePeriodClaims";

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
