namespace Subscription.DomainService.Reporting;

/// <summary>
/// How a usage report is bounded.
/// </summary>
/// <remarks>
/// <see cref="MeterKey"/> is nullable and means "every meter" when absent. It is a parameter and
/// never a constant because a meter key is a tenant's word, not the platform's — this module is
/// built so that no domain term ever reaches it, and a report that hardcoded one would be the
/// first place it did.
/// </remarks>
public sealed class GetUsageReportRequest
{
    public string? MeterKey { get; set; }

    /// <summary>Inclusive. Defaults to the first instant of the current UTC month.</summary>
    public DateTime? FromUtc { get; set; }

    /// <summary>Exclusive. Defaults to the first instant of the next UTC month.</summary>
    public DateTime? ToUtc { get; set; }

    /// <summary>
    /// <c>day</c> or <c>month</c>. Anything else is refused rather than guessed at: a silent
    /// fallback to one granularity when the caller asked for the other produces a chart that is
    /// wrong without looking wrong.
    /// </summary>
    public string? Granularity { get; set; }
}

/// <summary>
/// How a revenue or coupon report is bounded. Both read documents by the date they were issued.
/// </summary>
public sealed class GetRevenueReportRequest
{
    /// <summary>Inclusive. Defaults to the first instant of the current UTC month.</summary>
    public DateTime? FromUtc { get; set; }

    /// <summary>Exclusive. Defaults to the first instant of the next UTC month.</summary>
    public DateTime? ToUtc { get; set; }
}

/// <summary>
/// How the subscription roster is paged.
/// </summary>
public sealed class GetSubscriptionReportRequest
{
    public int PageSize { get; set; } = 25;

    public string? After { get; set; }
}
