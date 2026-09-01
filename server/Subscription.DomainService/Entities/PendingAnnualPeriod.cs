using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The year a calendar-aligned yearly subscription has bought but not yet started.
/// </summary>
/// <remarks>
/// Exists only between a mid-month signup and the first of the following month. A subscriber who
/// joins on 25 August holds a stub covering the rest of August and a year that begins on
/// 1 September; this is that year, waiting.
/// <para>
/// Every figure is frozen when the checkout is created, and none of them is recalculated when the
/// boundary arrives. The whole point of collecting them here is that the subscriber has been quoted
/// a year — its dates, its amount, its tax — and a boundary charge that re-derived any of it could
/// take a different sum than the one they agreed to.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PendingAnnualPeriod
{
    /// <summary>The local first the year begins on, and the same date a year later.</summary>
    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    /// <summary>What the year costs the payer, after discounts, tax and any credit.</summary>
    public long AmountMinor { get; set; }

    /// <summary>The charge before tax, and the tax on it — the split an invoice has to show.</summary>
    public long NetAmountMinor { get; set; }

    public long TaxAmountMinor { get; set; }

    /// <summary>The undiscounted annual amount, before anything came off it.</summary>
    public long GrossAmountMinor { get; set; }

    /// <summary>What the price's automatic discount and volume band took off.</summary>
    public long BuiltInDiscountMinor { get; set; }

    /// <summary>What a promotional code took off this annual term.</summary>
    public long PromotionalDiscountMinor { get; set; }

    /// <summary>
    /// Whether a promotional code actually reduced this year, so it can be counted once against
    /// <see cref="DiscountTerms.DurationPeriods"/> when the year is settled.
    /// </summary>
    public bool DiscountApplied { get; set; }

    /// <summary>
    /// Whether this year was <em>meant</em> to be collected with the opening charge.
    /// </summary>
    /// <remarks>
    /// The price's configuration, copied at signup. It says what was billed for, not what was
    /// paid — an unpaid checkout carries this exactly as a paid one does.
    /// </remarks>
    public bool CollectedWithCheckout { get; set; }

    /// <summary>
    /// Whether the money for this year has actually arrived.
    /// </summary>
    /// <remarks>
    /// Set by the activation that records the opening payment, never at signup. The distinction
    /// from <see cref="CollectedWithCheckout"/> is load-bearing twice over: this flag is what tells
    /// the boundary to skip the gateway, so deriving it from configuration would let an unpaid
    /// checkout open a year nobody paid for — and it is what a client reads to say whether the
    /// subscriber owes anything, so a pending checkout must not report itself as settled.
    /// </remarks>
    public bool IsPrepaid { get; set; }

    /// <summary>The payment that settled this year, once one has. Null while it is still owed.</summary>
    public string? PaymentDetailId { get; set; }

    /// <summary>
    /// A copy of this year naming <paramref name="paymentDetailId"/> as the payment that settled
    /// it, or an unchanged copy when no payment was taken.
    /// </summary>
    /// <remarks>
    /// A copy rather than a mutation, and applied at promotion rather than at reservation, because
    /// the confirmed payment does not exist yet when the reservation is written. Mutating the
    /// reserved instance afterwards changes only the copy in memory — the one already persisted
    /// keeps the old id — so the request path and the recovery sweep would install different
    /// payment references for the same settled operation, and which one a subscription ended up
    /// with would depend on whether a process happened to die.
    /// <para>
    /// The adjustment's own payment, not the original year's: after a settlement the frozen
    /// figures describe the new terms, and pointing at the payment that bought the old ones would
    /// name an invoice for an amount this year no longer says it costs.
    /// </para>
    /// </remarks>
    public PendingAnnualPeriod SettledBy(string? paymentDetailId) => new()
    {
        StartUtc = StartUtc,
        EndUtc = EndUtc,
        AmountMinor = AmountMinor,
        NetAmountMinor = NetAmountMinor,
        TaxAmountMinor = TaxAmountMinor,
        GrossAmountMinor = GrossAmountMinor,
        BuiltInDiscountMinor = BuiltInDiscountMinor,
        PromotionalDiscountMinor = PromotionalDiscountMinor,
        DiscountApplied = DiscountApplied,
        CollectedWithCheckout = CollectedWithCheckout,
        IsPrepaid = IsPrepaid,
        // Nothing was charged — a settlement covered entirely by credit, say — so the payment that
        // settled this year is still whichever one did.
        PaymentDetailId = paymentDetailId is { Length: > 0 } ? paymentDetailId : PaymentDetailId
    };
}
