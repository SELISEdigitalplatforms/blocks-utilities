using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Reporting;

/// <summary>
/// Read-only aggregations over the subscription collections.
/// </summary>
/// <remarks>
/// Every method takes a tenant and every query is filtered by it. Reporting is the one place in
/// this module that reads across organizations, so tenant isolation stops being a side effect of
/// the query shape and becomes the only thing holding — which is why it is stated on every
/// signature rather than inherited from ambient context.
/// <para>
/// Nothing here writes. The collections it reads are the live operational ones, so a report is
/// always current and there is no rollup to fall behind; the cost is that a wide date range is a
/// wide scan, which is why the service caps the range before calling any of this.
/// </para>
/// </remarks>
public interface ISubscriptionReportingRepository
{
    /// <summary>
    /// Ledger volume grouped by meter, time bucket and entry type.
    /// </summary>
    /// <remarks>
    /// Entry type stays in the grouping key rather than being folded into a single net figure by
    /// the database. Consumption, reversal and grant answer different questions, and a caller that
    /// wants the net can add three numbers whereas a caller given only the net can never get the
    /// three back.
    /// </remarks>
    Task<IReadOnlyList<UsageLedgerBucket>> AggregateUsageAsync(
        string tenantId,
        string? meterKey,
        DateTime fromUtc,
        DateTime toUtc,
        bool byMonth,
        CancellationToken cancellationToken);

    /// <summary>Every subscription in the tenant holding one of the given statuses.</summary>
    Task<IReadOnlyList<SubscriptionDetail>> ListByStatusAsync(
        string tenantId,
        IReadOnlyCollection<SubscriptionStatus> statuses,
        CancellationToken cancellationToken);

    /// <summary>Financial document totals over a window, grouped by currency and document type.</summary>
    Task<IReadOnlyList<DocumentRevenueBucket>> AggregateDocumentRevenueAsync(
        string tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    /// <summary>Charged usage-invoice totals over a window, grouped by currency.</summary>
    Task<IReadOnlyList<OverageRevenueBucket>> AggregateOverageRevenueAsync(
        string tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    /// <summary>Usage invoices still being retried.</summary>
    Task<IReadOnlyList<SubscriptionUsageInvoice>> ListRetryingUsageInvoicesAsync(
        string tenantId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>One keyset page of the tenant's subscriptions, newest first.</summary>
    Task<SubscriptionRosterPage> ListSubscriptionsAsync(
        string tenantId,
        int pageSize,
        SubscriptionReportCursor? after,
        CancellationToken cancellationToken);

    /// <summary>
    /// Published current-usage rows for the named subscriptions.
    /// </summary>
    /// <remarks>
    /// Rows the projection has not published, or whose period has expired out from under their
    /// TTL, simply do not come back. The caller reports that absence rather than substituting a
    /// zero.
    /// </remarks>
    Task<IReadOnlyList<SubscriptionUsageCurrent>> ListCurrentUsageAsync(
        string tenantId,
        IReadOnlyCollection<string> subscriptionIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Coupon uptake, grouped by code and organization.
    /// </summary>
    /// <remarks>
    /// Grouped by organization as well as code so the caller can count distinct organizations
    /// without pulling every subscription back. Read from the discount recorded on the
    /// subscription, which is the only source covering non-campaign codes.
    /// </remarks>
    Task<IReadOnlyList<CouponUptakeBucket>> AggregateCouponUptakeAsync(
        string tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    /// <summary>Billed amounts on documents carrying a promotion code, grouped by code and currency.</summary>
    Task<IReadOnlyList<CouponRevenueBucket>> AggregateCouponRevenueAsync(
        string tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    /// <summary>Campaign redemption counts, grouped by discount and state.</summary>
    Task<IReadOnlyList<CampaignRedemptionBucket>> AggregateCampaignRedemptionsAsync(
        string tenantId,
        CancellationToken cancellationToken);

    /// <summary>Discount codes for the given discount ids, so redemptions can be reported by code.</summary>
    Task<IReadOnlyDictionary<string, string>> GetDiscountCodesAsync(
        string tenantId,
        IReadOnlyCollection<string> discountIds,
        CancellationToken cancellationToken);
}

/// <summary>One meter's ledger volume of one entry type in one time bucket.</summary>
public sealed record UsageLedgerBucket(
    string MeterKey,
    DateTime BucketStartUtc,
    UsageEntryType EntryType,
    decimal Quantity,
    long RecordCount);

/// <summary>Financial document totals for one currency and document type.</summary>
public sealed record DocumentRevenueBucket(
    string CurrencyCode,
    FinancialDocumentType DocumentType,
    long TotalMinor,
    long DocumentCount);

/// <summary>Charged usage-invoice totals for one currency.</summary>
public sealed record OverageRevenueBucket(
    string CurrencyCode,
    long TotalMinor,
    long InvoiceCount);

/// <summary>One coupon code's uptake within one organization.</summary>
public sealed record CouponUptakeBucket(
    string Code,
    string OrganizationId,
    long SubscriptionCount);

/// <summary>Billed amounts for one promotion code in one currency.</summary>
public sealed record CouponRevenueBucket(
    string Code,
    string CurrencyCode,
    long RevenueMinor,
    long DiscountGivenMinor,
    long DocumentCount);

/// <summary>How many redemptions one campaign holds in one state.</summary>
public sealed record CampaignRedemptionBucket(
    string DiscountId,
    CampaignRedemptionState State,
    long Count);

/// <summary>One page of subscriptions, plus whether another follows.</summary>
public sealed record SubscriptionRosterPage(
    IReadOnlyList<SubscriptionDetail> Items,
    bool HasMore);
