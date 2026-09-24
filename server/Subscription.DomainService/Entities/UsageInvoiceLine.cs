using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One meter's contribution to a usage invoice's total.
/// </summary>
/// <remarks>
/// Support traceability only — the charge itself is always the invoice's single aggregated
/// total, never one charge per line, so a decline or a dashboard entry doesn't fragment across
/// meters that happened to overage in the same period.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class UsageInvoiceLine
{
    public string MeterKey { get; set; } = string.Empty;

    public decimal OverageQuantity { get; set; }

    /// <summary>
    /// The window's allowance the overage was measured against -- a trial grant or carried-forward
    /// allowance included -- as rating saw it. Null on lines rated before it was recorded.
    /// </summary>
    /// <remarks>
    /// Kept so the invoice can say what the charge is: usage past what the plan included. Only
    /// rating knows the allowance a window actually had; recomputing it at issue time would read
    /// today's plan and trial, not the terms the overage was priced under.
    /// </remarks>
    public decimal? IncludedQuantity { get; set; }

    /// <summary>Everything used in the window, as rated. Null on lines rated before it was recorded.</summary>
    public decimal? UsedQuantity { get; set; }

    public long AmountMinor { get; set; }
}
