namespace Subscription.DomainService.Entities;

/// <summary>
/// Immutable rating terms for a usage window cut short by a plan change.
/// Stored on the subscription in the same compare-and-set that installs the new schedule.
/// </summary>
public sealed class PendingUsagePeriod
{
    public string PeriodKey { get; set; } = string.Empty;
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public PlanSnapshot Plan { get; set; } = new();
    public PriceSnapshot Price { get; set; } = new();
    public string CurrencyCode { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}
