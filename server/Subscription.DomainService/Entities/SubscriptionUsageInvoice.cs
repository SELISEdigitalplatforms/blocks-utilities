using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One usage period's overage, priced and tracked toward being charged.
/// </summary>
/// <remarks>
/// Created before the charge is attempted, the same discipline
/// <see cref="SubscriptionPaymentLink"/> uses for the initial charge — a crash mid-attempt is
/// recoverable by re-reading this same record rather than losing track of what was already
/// billed or double-charging a retry.
/// <para>
/// Deliberately its own invoice, independent of the fee renewal: a decline here never touches
/// the subscription's <c>Status</c> or the fee-side dunning cycle. See the module README.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionUsageInvoice
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public string PeriodKey { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = string.Empty;

    public long TotalAmountMinor { get; set; }

    /// <summary>The portion of <see cref="TotalAmountMinor"/> that is tax.</summary>
    /// <remarks>
    /// Recorded rather than recomputed. The rate and mode below are the subscription's snapshot at
    /// the moment this invoice was raised, so a catalogue edit afterwards cannot make a charged
    /// invoice describe itself differently.
    /// </remarks>
    public long TaxAmountMinor { get; set; }

    /// <summary>What was taxed. Equals <see cref="TotalAmountMinor"/> when there is no tax.</summary>
    public long NetAmountMinor { get; set; }

    public int? TaxRateBasisPoints { get; set; }

    public TaxMode? TaxMode { get; set; }

    public List<UsageInvoiceLine> Lines { get; set; } = [];

    public SubscriptionUsageInvoiceState State { get; set; } =
        SubscriptionUsageInvoiceState.Pending;

    public int AttemptCount { get; set; }

    /// <summary>When the charge sweep should look at this invoice again. Null once terminal.</summary>
    public DateTime? NextAttemptAtUtc { get; set; }

    public string? PaymentDetailId { get; set; }

    public string? LastError { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;
}
