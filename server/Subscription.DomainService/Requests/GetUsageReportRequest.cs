namespace Subscription.DomainService.Requests;

/// <summary>
/// The query shared by every tenant-usage-analytics report endpoint.
/// </summary>
/// <remarks>
/// Tenant-scoped-with-optional-organization-filter, not organization-scoped like every other
/// subscription read: <see cref="OrganizationId"/> narrows within the caller's tenant rather than
/// naming which organization the caller acts as. See
/// <c>SubscriptionUsageReportsController</c>'s own remarks for why that is a materially different
/// question from <c>PaymentOrganizationScope.RequestMayNameOrganization</c>.
/// </remarks>
public sealed class GetUsageReportRequest
{
    /// <summary>
    /// "Day", "Week", "Month" or "Year", case-insensitive. Unrecognised is a domain error rather
    /// than a silent fallback — see <c>SubscriptionUsageReportService.TryParseGranularity</c>.
    /// </summary>
    public string? Granularity { get; set; }

    /// <summary>Inclusive lower bound on when usage occurred. Null means "from the beginning".</summary>
    public DateTime? FromUtc { get; set; }

    /// <summary>Inclusive upper bound on when usage occurred. Null means "through now".</summary>
    public DateTime? ToUtc { get; set; }

    /// <summary>A filter within the tenant, never a scope escalation. Null lists every organization.</summary>
    public string? OrganizationId { get; set; }

    public string? MeterKey { get; set; }

    public string? SubscriptionId { get; set; }

    public int PageSize { get; set; } = 25;

    public string? After { get; set; }
}
