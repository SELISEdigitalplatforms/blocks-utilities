using MongoDB.Bson.Serialization.Attributes;

namespace Payment.DomainService.Entities;

/// <summary>
/// How a subscription settlement's amount was arrived at: the period being left, the period being
/// joined, and what closed the gap.
/// </summary>
/// <remarks>
/// A settlement is not a discounted price — it is a subtraction between two prorated periods, so the
/// flat gross-and-discount fields a renewal records cannot describe one. A subscriber asking why they
/// were charged CHF 41.30 mid-month is asking about the two sides, not the remainder.
/// <para>
/// Lives in the payment module beside <see cref="PaymentDetail"/> because that is where it is stored,
/// and stored at all because it cannot be recomputed later: the catalogue moves, and the instant the
/// change was quoted at is gone.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionSettlementBreakdown
{
    /// <summary>The period the subscriber is leaving part-way through.</summary>
    public SubscriptionSettlementSide Outgoing { get; set; } = new();

    /// <summary>The period they are joining.</summary>
    public SubscriptionSettlementSide Target { get; set; } = new();

    /// <summary>Banked credit spent against the difference. Zero on a downgrade, which banks more.</summary>
    public long CreditConsumedMinor { get; set; }

    /// <summary>
    /// Target prorated value less outgoing unused value less credit. Negative where a downgrade
    /// banked credit instead of charging, which is why it is not simply the amount charged.
    /// </summary>
    public long NetSettlementMinor { get; set; }

    /// <summary>
    /// A second settlement, when this change also settles a prepaid annual period alongside the
    /// opening stub it replaces. Null for every ordinary settlement, which is exactly one period.
    /// </summary>
    /// <remarks>
    /// An upgrade taken during a calendar-aligned yearly subscription's opening stub, once that
    /// stub's own year has already been paid for, settles two things at once: the days left on the
    /// stub, and the difference between what the paid year cost and what it costs on the new terms.
    /// The two are never netted before tax — a plan change can move the subscriber between tax
    /// modes, so each side is its own subtraction with its own tax, and this is the second one.
    /// <para>
    /// <see cref="Outgoing"/>/<see cref="Target"/> above describe the stub in this shape; this
    /// describes the annual period. Only the <em>top-level</em> <see cref="CreditConsumedMinor"/>
    /// and <see cref="NetSettlementMinor"/> are the figures actually charged — credit is spent once
    /// against the combined total, never against each side separately — so the same two fields on
    /// this nested breakdown describe the annual side's own contribution before that combination,
    /// for the invoice to explain rather than for anything to re-derive a charge from.
    /// </para>
    /// </remarks>
    public SubscriptionSettlementBreakdown? Annual { get; set; }
}

/// <summary>One side of a settlement, priced as its own period and then prorated.</summary>
[BsonIgnoreExtraElements]
public sealed class SubscriptionSettlementSide
{
    public long GrossAmountMinor { get; set; }

    /// <summary>The price's automatic discount and the volume band, combined as the price says.</summary>
    public long BuiltInDiscountMinor { get; set; }

    /// <summary>A promotional code, after the built-in reduction was settled.</summary>
    public long PromotionalDiscountMinor { get; set; }

    /// <summary>Tax at this side's own rate and mode — a change can cross between the two.</summary>
    public long TaxAmountMinor { get; set; }

    /// <summary>The whole period, tax included.</summary>
    public long PeriodTotalMinor { get; set; }

    /// <summary>
    /// The part of that this settlement counts: unused time on the outgoing side, remaining time on
    /// the target side.
    /// </summary>
    public long ProratedValueMinor { get; set; }
}
