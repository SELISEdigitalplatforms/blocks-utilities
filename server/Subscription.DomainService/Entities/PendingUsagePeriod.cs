namespace Subscription.DomainService.Entities;

/// <summary>
/// Immutable rating terms for a usage window cut short — by a plan change, or by a cancellation
/// that stops entitlement. Stored on the subscription in the same compare-and-set that makes the
/// change, so there is no window in which the write can land without the period also being
/// captured for the rating sweep to price afterward.
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

    /// <summary>
    /// Each resetting meter's effective allowance, frozen via
    /// <see cref="Services.IMeterAllowanceResolver.EffectiveAsync"/> against the subscription's
    /// state at the moment this period was captured — before the status/schedule transition
    /// (cancellation or plan change) that cut it short took effect. Keyed by
    /// <see cref="PlanMeter.MeterKey"/>.
    /// </summary>
    /// <remarks>
    /// Nullable for backward compatibility: a document queued before this field existed carries
    /// no snapshot, and final rating falls back to resolving the allowance live against the
    /// subscription's current (post-transition) state for those legacy documents — the same
    /// behavior this type had before the snapshot was added.
    /// </remarks>
    public Dictionary<string, long>? MeterAllowances { get; set; }
}
