namespace Subscription.DomainService.Responses;

/// <summary>Shared page info, matching <c>SubscriptionFinancialDocumentPageInfoResponse</c>'s shape.</summary>
public sealed class UsageReportPageInfoResponse
{
    public int PageSize { get; init; }

    public bool HasNextPage { get; init; }

    public string? NextCursor { get; init; }
}

/// <summary>Volume per bucket at the requested granularity — answers "how much, over time".</summary>
public sealed class UsageTimeseriesResponse
{
    public IReadOnlyList<UsageTimeseriesPointResponse> Items { get; init; } = [];

    public UsageReportPageInfoResponse PageInfo { get; init; } = new();
}

public sealed class UsageTimeseriesPointResponse
{
    public string PeriodKey { get; init; } = string.Empty;

    public DateTime PeriodStartUtc { get; init; }

    public decimal ConsumedQuantity { get; init; }

    public long EntryCount { get; init; }
}

/// <summary>Per-organization totals — answers "which organizations, how much".</summary>
public sealed class UsageOrganizationBreakdownResponse
{
    public IReadOnlyList<UsageOrganizationTotalResponse> Items { get; init; } = [];

    public UsageReportPageInfoResponse PageInfo { get; init; } = new();
}

public sealed class UsageOrganizationTotalResponse
{
    /// <summary>The Blocks organization id. Display name resolves client-side through IAM.</summary>
    public string OrganizationId { get; init; } = string.Empty;

    public decimal ConsumedQuantity { get; init; }

    public long EntryCount { get; init; }
}

/// <summary>Per-user totals — answers "who, how much".</summary>
public sealed class UsageActorBreakdownResponse
{
    public IReadOnlyList<UsageActorTotalResponse> Items { get; init; } = [];

    public UsageReportPageInfoResponse PageInfo { get; init; } = new();
}

public sealed class UsageActorTotalResponse
{
    public string OrganizationId { get; init; } = string.Empty;

    public string MeterKey { get; init; } = string.Empty;

    public DateTime DayUtc { get; init; }

    /// <summary>The Blocks user id. Display name resolves client-side through IAM.</summary>
    public string UserId { get; init; } = string.Empty;

    public decimal ConsumedQuantity { get; init; }

    public long EntryCount { get; init; }
}

/// <summary>
/// Per-period allowance, plan, footprint and overage — answers "how much was included, used and
/// charged for".
/// </summary>
public sealed class UsageAllowanceHistoryResponse
{
    public IReadOnlyList<UsageAllowancePeriodResponse> Items { get; init; } = [];

    public UsageReportPageInfoResponse PageInfo { get; init; } = new();
}

public sealed class UsageAllowancePeriodResponse
{
    public string OrganizationId { get; init; } = string.Empty;

    public string SubscriptionId { get; init; } = string.Empty;

    public string PeriodKey { get; init; } = string.Empty;

    /// <summary><c>true</c> for the subscription's still-open window, <c>false</c> for a closed one.</summary>
    public bool IsOpenPeriod { get; init; }

    public IReadOnlyList<UsageAllowanceMeterResponse> Meters { get; init; } = [];
}

public sealed class UsageAllowanceMeterResponse
{
    public string MeterKey { get; init; } = string.Empty;

    public string PlanId { get; init; } = string.Empty;

    public string PlanCode { get; init; } = string.Empty;

    public decimal IncludedQuantity { get; init; }

    public decimal UsedQuantity { get; init; }

    public decimal OverageQuantity { get; init; }

    public long? OverageAmountMinor { get; init; }

    /// <summary>
    /// <c>true</c> for a closed period rated before the invoice line carried
    /// <c>IncludedQuantity</c>/<c>UsedQuantity</c>. Its allowance and usage footprint cannot be
    /// reconstructed retroactively, so only the overage it was actually charged for is reported —
    /// see the module's own remarks on why no backfill invents a number here.
    /// </summary>
    public bool IsHistoricalOverageOnly { get; init; }
}
