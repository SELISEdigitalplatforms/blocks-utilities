namespace Subscription.DomainService.Reporting;

/// <summary>
/// Metered volume over time, one entry per meter per bucket.
/// </summary>
/// <remarks>
/// Every money figure in this file is grouped by currency and never summed across currencies.
/// There is no exchange-rate source anywhere in this module and a subscription's currency is
/// fixed for its life, so a single total would be an addition of unlike things — and one nobody
/// downstream could detect, because it would still be a number. When rates arrive, a converted
/// total is a field added beside these, not a change to them.
/// </remarks>
public sealed class UsageReportResponse
{
    public DateTime FromUtc { get; init; }

    public DateTime ToUtc { get; init; }

    public string Granularity { get; init; } = string.Empty;

    public IReadOnlyList<UsageReportBucketResponse> Buckets { get; init; } = [];
}

/// <summary>
/// One meter's volume in one time bucket.
/// </summary>
/// <remarks>
/// The four figures are reported separately rather than as one net number because they answer
/// different questions. A month that looks low is either a quiet month or a month somebody
/// reversed, and a single total cannot tell the two apart.
/// <para>
/// Grants are excluded from <see cref="NetQuantity"/> on purpose: a grant raises an allowance,
/// it is not something a subscriber consumed. Counting one as usage would inflate the very
/// number this report exists to state. It is reported anyway so that nothing in the ledger is
/// silently dropped.
/// </para>
/// </remarks>
public sealed class UsageReportBucketResponse
{
    public string MeterKey { get; init; } = string.Empty;

    /// <summary>The bucket's first instant — midnight for a day, the first of the month for a month.</summary>
    public DateTime BucketStartUtc { get; init; }

    /// <summary>What was consumed, before any reversal.</summary>
    public decimal ConsumedQuantity { get; init; }

    /// <summary>What was reversed, as a positive number.</summary>
    public decimal ReversedQuantity { get; init; }

    /// <summary>Consumption less reversals. The figure to report as "usage".</summary>
    public decimal NetQuantity { get; init; }

    /// <summary>Allowance granted in this bucket. Not usage — see the type's remarks.</summary>
    public decimal GrantedQuantity { get; init; }

    /// <summary>How many ledger entries produced these figures.</summary>
    public long RecordCount { get; init; }
}

/// <summary>
/// Recurring run-rate, grouped by currency and then by seat tier.
/// </summary>
public sealed class RecurringRevenueReportResponse
{
    /// <summary>Live subscriptions the figures were computed from.</summary>
    public long SubscriptionCount { get; init; }

    public IReadOnlyList<RecurringRevenueCurrencyResponse> Currencies { get; init; } = [];
}

public sealed class RecurringRevenueCurrencyResponse
{
    public string CurrencyCode { get; init; } = string.Empty;

    public long GrossMonthlyMinor { get; init; }

    public long NetMonthlyMinor { get; init; }

    public long GrossAnnualMinor { get; init; }

    public long NetAnnualMinor { get; init; }

    public long SubscriptionCount { get; init; }

    public IReadOnlyList<RecurringRevenueTierResponse> Tiers { get; init; } = [];
}

/// <summary>
/// One seat-count band's contribution to the run-rate.
/// </summary>
/// <remarks>
/// <see cref="NetMonthlyMinor"/> is after promotional discount and is the figure that matches
/// what renewal will actually charge; <see cref="GrossMonthlyMinor"/> is list price. Both are
/// reported because the gap between them is the cost of the discounts currently being honoured,
/// which is not visible from either figure alone.
/// </remarks>
public sealed class RecurringRevenueTierResponse
{
    /// <summary>The band, as configured: <c>1</c>, <c>2-3</c>, <c>4-9</c>, <c>10-24</c>, <c>25-40</c>, <c>41+</c>.</summary>
    public string Tier { get; init; } = string.Empty;

    public int MinimumSeats { get; init; }

    /// <summary>Null on the open-ended top band.</summary>
    public int? MaximumSeats { get; init; }

    public long SubscriptionCount { get; init; }

    public long SeatCount { get; init; }

    public long GrossMonthlyMinor { get; init; }

    public long NetMonthlyMinor { get; init; }

    public long GrossAnnualMinor { get; init; }

    public long NetAnnualMinor { get; init; }
}

/// <summary>
/// Billed revenue over a window, split by where it came from.
/// </summary>
/// <remarks>
/// The split is exact rather than inferred: recurring charges produce financial documents and
/// metered overage produces usage invoices, so the two live in different collections and no line
/// item has to be parsed to tell them apart.
/// </remarks>
public sealed class RevenueReportResponse
{
    public DateTime FromUtc { get; init; }

    public DateTime ToUtc { get; init; }

    public IReadOnlyList<RevenueCurrencyResponse> Currencies { get; init; } = [];
}

public sealed class RevenueCurrencyResponse
{
    public string CurrencyCode { get; init; } = string.Empty;

    /// <summary>Invoices and trial invoices — the recurring subscription fee.</summary>
    public long SubscriptionRevenueMinor { get; init; }

    /// <summary>Charged usage invoices — metered overage.</summary>
    public long OverageRevenueMinor { get; init; }

    public long TotalRevenueMinor { get; init; }

    /// <summary>
    /// Credit notes issued in the window, as a positive number.
    /// </summary>
    /// <remarks>
    /// Reported beside the revenue rather than subtracted from it. A credit note commonly refunds
    /// a charge from an earlier window, so netting it here would quietly reduce a month that did
    /// not earn it — and would make the figure disagree with the invoices it claims to total.
    /// </remarks>
    public long CreditNoteMinor { get; init; }

    public long SubscriptionDocumentCount { get; init; }

    public long OverageInvoiceCount { get; init; }
}

/// <summary>
/// What is failing to collect, and what the system intends to do about it.
/// </summary>
/// <remarks>
/// Named for dunning rather than for overdue invoices because this module has no accounts
/// receivable. A financial document is issued only once a charge has settled, and
/// <c>FinancialDocumentStatus</c> has no unpaid state — an invoice here is a receipt. What is
/// actually outstanding is a subscription in its dunning cycle and a usage invoice still being
/// retried, which is what these two lists are.
/// </remarks>
public sealed class DunningReportResponse
{
    public IReadOnlyList<DunningSubscriptionResponse> Subscriptions { get; init; } = [];

    public IReadOnlyList<DunningUsageInvoiceResponse> UsageInvoices { get; init; } = [];

    public long SubscriptionCount { get; init; }

    public long UsageInvoiceCount { get; init; }
}

public sealed class DunningSubscriptionResponse
{
    public string SubscriptionId { get; init; } = string.Empty;

    public string OrganizationId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string PlanCode { get; init; } = string.Empty;

    public string CurrencyCode { get; init; } = string.Empty;

    /// <summary>List price for one period, before discount. Zero where no price is snapshotted.</summary>
    public long PeriodAmountMinor { get; init; }

    public DateTime? PastDueSinceUtc { get; init; }

    public int DunningAttemptCount { get; init; }

    public DateTime? NextFeeBillingAtUtc { get; init; }

    public DateTime CurrentPeriodEndUtc { get; init; }
}

public sealed class DunningUsageInvoiceResponse
{
    public string UsageInvoiceId { get; init; } = string.Empty;

    public string SubscriptionId { get; init; } = string.Empty;

    public string OrganizationId { get; init; } = string.Empty;

    public string PeriodKey { get; init; } = string.Empty;

    public string CurrencyCode { get; init; } = string.Empty;

    public long TotalAmountMinor { get; init; }

    public int AttemptCount { get; init; }

    public DateTime? NextAttemptAtUtc { get; init; }

    public string? LastError { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

/// <summary>
/// One page of the subscription roster.
/// </summary>
public sealed class SubscriptionRosterReportResponse
{
    public IReadOnlyList<SubscriptionRosterRowResponse> Items { get; init; } = [];

    public SubscriptionReportPageInfoResponse PageInfo { get; init; } = new();
}

public sealed class SubscriptionReportPageInfoResponse
{
    public int PageSize { get; init; }

    public bool HasNextPage { get; init; }

    public string? NextCursor { get; init; }
}

public sealed class SubscriptionRosterRowResponse
{
    public string SubscriptionId { get; init; } = string.Empty;

    public string OrganizationId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string PlanCode { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    /// <summary>The seat tier this subscription falls in — the same bands the revenue report uses.</summary>
    public string SeatTier { get; init; } = string.Empty;

    public long SeatCount { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public long GrossMonthlyMinor { get; init; }

    public long NetMonthlyMinor { get; init; }

    public bool CancelAtPeriodEnd { get; init; }

    public DateTime CurrentPeriodEndUtc { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    /// <summary>
    /// Per-meter consumption against allowance for the period running now.
    /// </summary>
    /// <remarks>
    /// Empty when the projection has published nothing for this subscription. That is reported as
    /// an absence rather than as zero usage, because the two mean opposite things to whoever reads
    /// the report and only one of them is a reason to act.
    /// </remarks>
    public IReadOnlyList<SubscriptionRosterMeterResponse> Meters { get; init; } = [];
}

/// <summary>
/// One meter's standing on one subscription, for the period running now.
/// </summary>
/// <remarks>
/// Read from the published current-usage projection, so it can lag the counters by however long
/// the last publish took. <see cref="UpdatedAtUtc"/> is included so a reader can see how stale a
/// figure is instead of having to assume. This is a report, not the enforcement gate: only
/// <c>POST /api/subscription-usage</c> settles whether a unit may be consumed.
/// </remarks>
public sealed class SubscriptionRosterMeterResponse
{
    public string MeterKey { get; init; } = string.Empty;

    public string PeriodKey { get; init; } = string.Empty;

    public decimal Included { get; init; }

    public decimal Used { get; init; }

    public decimal Remaining { get; init; }

    public decimal Overage { get; init; }

    /// <summary>
    /// Used as a percentage of the allowance, rounded to two places. Null when the allowance is
    /// zero or unlimited, where a percentage has no meaning and a zero would be read as "unused".
    /// </summary>
    public decimal? UsedPercentOfQuota { get; init; }

    public bool OverageAllowed { get; init; }

    public DateTime UpdatedAtUtc { get; init; }
}

/// <summary>
/// Coupon uptake and what it cost, one entry per code.
/// </summary>
public sealed class CouponReportResponse
{
    public DateTime FromUtc { get; init; }

    public DateTime ToUtc { get; init; }

    public IReadOnlyList<CouponReportRowResponse> Coupons { get; init; } = [];
}

/// <summary>
/// One coupon code's uptake and revenue.
/// </summary>
/// <remarks>
/// Uptake is counted from the discount recorded on subscriptions, not from the campaign
/// redemption ledger. Redemptions are written only for campaign-kind discounts, so counting from
/// there would report zero for every ordinary promotional code — a wrong answer that looks like a
/// real one.
/// <para>
/// <see cref="RedeemingOrganizations"/> counts organizations, because that is what every record
/// involved is keyed to. A per-user count would be a different figure and is deliberately not
/// implied by this one's name.
/// </para>
/// </remarks>
public sealed class CouponReportRowResponse
{
    public string Code { get; init; } = string.Empty;

    /// <summary>Distinct organizations that have a subscription carrying this code.</summary>
    public long RedeemingOrganizations { get; init; }

    /// <summary>Subscriptions carrying this code, including several for one organization over time.</summary>
    public long SubscriptionCount { get; init; }

    /// <summary>
    /// Campaign redemption states held for this code, where any exist.
    /// </summary>
    /// <remarks>
    /// Empty for an ordinary promotional code. That is a real distinction — such a code has no
    /// one-use rule to enforce and therefore nothing to reserve — not missing data.
    /// </remarks>
    public IReadOnlyList<CouponRedemptionStateResponse> CampaignStates { get; init; } = [];

    public IReadOnlyList<CouponCurrencyResponse> Currencies { get; init; } = [];
}

public sealed class CouponRedemptionStateResponse
{
    public string State { get; init; } = string.Empty;

    public long Count { get; init; }
}

public sealed class CouponCurrencyResponse
{
    public string CurrencyCode { get; init; } = string.Empty;

    /// <summary>What was actually billed on documents carrying this code.</summary>
    public long RevenueMinor { get; init; }

    /// <summary>What the code took off those documents.</summary>
    public long DiscountGivenMinor { get; init; }

    public long DocumentCount { get; init; }
}
