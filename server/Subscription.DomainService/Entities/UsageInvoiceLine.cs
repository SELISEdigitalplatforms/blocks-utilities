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

    public long AmountMinor { get; set; }
}
