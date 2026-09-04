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

    /// <summary>
    /// The published current-usage projection, read directly by consumers outside this service.
    /// </summary>
    /// <remarks>
    /// Its own collection rather than fields on the counter, so a consumer can be granted read
    /// access to exactly this and nothing else. Granting a reader the counter collection would
    /// hand it the enforcement authority for metered billing.
    /// </remarks>
    public const string UsageCurrent = "SubscriptionUsageCurrent";
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
    public const string CampaignRedemptions = "SubscriptionCampaignRedemptions";

    /// <summary>
    /// One document per subscription, meter and day — the precomputed volume-over-time and
    /// per-organization view behind the tenant-admin usage report. Derived from
    /// <see cref="UsageRecords"/> and disposable; never authoritative.
    /// </summary>
    public const string UsageActivityRollups = "SubscriptionUsageActivityRollups";

    /// <summary>
    /// One document per organization, meter, day and user — the per-actor breakdown behind the
    /// tenant-admin usage report. Its own collection rather than a map embedded on
    /// <see cref="UsageActivityRollups"/>, so a busy organization's headcount cannot grow one
    /// document past Mongo's size limit.
    /// </summary>
    public const string UsageActorRollups = "SubscriptionUsageActorRollups";

    /// <summary>
    /// Append-only record of every mail handed to the listener, payload included. Separate from the
    /// documents it reports on, because it also covers mail that has no document behind it.
    /// </summary>
    public const string MailDeliveryReports = "SubscriptionMailDeliveryReports";

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
