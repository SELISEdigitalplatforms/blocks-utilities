using System.ComponentModel;

namespace Subscription.DomainService.Reporting;

/// <summary>
/// How a usage report is bounded.
/// </summary>
/// <remarks>
/// <see cref="MeterKey"/> is nullable and means "every meter" when absent. It is a parameter and
/// never a constant because a meter key is a tenant's word, not the platform's — this module is
/// built so that no domain term ever reaches it, and a report that hardcoded one would be the
/// first place it did.
/// <para>
/// Every property also carries <see cref="DescriptionAttribute"/>, which is not decoration. Only
/// the Api assembly emits an XML documentation file, so the XML comments in this project never
/// reach the OpenAPI document and these parameters would otherwise arrive at Swagger and Scalar
/// with blank descriptions. <c>[Description]</c> is read from the attribute itself and does reach
/// them. It lives in <c>System.ComponentModel</c>, so saying this costs this project no reference
/// — binding the name with an MVC attribute would, and no other type in any domain project here
/// knows about ASP.NET Core.
/// </para>
/// </remarks>
public sealed class GetUsageReportRequest
{
    [Description(
        "One meter to report on. Omit for every meter. A meter key is the tenant's own word, " +
        "never one this API knows.")]
    public string? MeterKey { get; set; }

    /// <summary>Inclusive. Defaults to the first instant of the current UTC month.</summary>
    [Description(
        "Start of the window, inclusive. Defaults to the first instant of the current UTC month.")]
    public DateTime? FromUtc { get; set; }

    /// <summary>Exclusive. Defaults to the first instant of the next UTC month.</summary>
    [Description(
        "End of the window, exclusive, so consecutive windows tile exactly. Defaults to the " +
        "first instant of the next UTC month. The window may not exceed 366 days.")]
    public DateTime? ToUtc { get; set; }

    /// <summary>
    /// <c>day</c> or <c>month</c>. Anything else is refused rather than guessed at: a silent
    /// fallback to one granularity when the caller asked for the other produces a chart that is
    /// wrong without looking wrong.
    /// </summary>
    [Description(
        "Bucket size: 'day' or 'month'. Defaults to 'month'. Any other value is refused rather " +
        "than guessed at.")]
    public string? Granularity { get; set; }
}

/// <summary>
/// How a revenue or coupon report is bounded. Both read documents by the date they were issued.
/// </summary>
public sealed class GetRevenueReportRequest
{
    /// <summary>Inclusive. Defaults to the first instant of the current UTC month.</summary>
    [Description(
        "Start of the window, inclusive. Defaults to the first instant of the current UTC month.")]
    public DateTime? FromUtc { get; set; }

    /// <summary>Exclusive. Defaults to the first instant of the next UTC month.</summary>
    [Description(
        "End of the window, exclusive, so consecutive windows tile exactly. Defaults to the " +
        "first instant of the next UTC month. The window may not exceed 366 days.")]
    public DateTime? ToUtc { get; set; }
}

/// <summary>
/// How the subscription roster is paged.
/// </summary>
public sealed class GetSubscriptionReportRequest
{
    [Description("Rows per page, 1 to 100. Defaults to 25.")]
    public int PageSize { get; set; } = 25;

    /// <summary>
    /// The opaque cursor from the previous page's <c>pageInfo.nextCursor</c>.
    /// </summary>
    /// <remarks>
    /// Bound to the tenant it was issued for. A cursor is a value the client holds and can edit,
    /// so one presented by another tenant is refused rather than quietly paging through their
    /// subscriptions.
    /// </remarks>
    [Description(
        "Opaque cursor from the previous page's pageInfo.nextCursor. Bound to the tenant it was " +
        "issued for; a cursor from another tenant is refused.")]
    public string? After { get; set; }
}
