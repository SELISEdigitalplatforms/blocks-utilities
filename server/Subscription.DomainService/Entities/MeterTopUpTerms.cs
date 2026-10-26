using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The terms on which extra units of one meter may be bought outright, on top of what the plan
/// includes.
/// </summary>
/// <remarks>
/// Separate from <see cref="PlanMeter.RateTables"/>, which price usage <em>after</em> it has
/// happened. A top-up is paid for before it is used and is held as a grant until it is consumed,
/// expires or is forfeited — see spec 002. The two can coexist on one meter: credits are drawn
/// before overage is.
/// <para>
/// Null on every meter authored before purchasable credits existed, which reads as "not for sale"
/// and changes nothing about how that meter counts.
/// </para>
/// <para>
/// Snapshotted onto the subscription with the rest of the plan, so a price raised in the catalogue
/// reaches new subscribers only. A purchase is quoted from the subscriber's own copy. The one
/// exception is a snapshot with no terms at all: when the catalogue starts selling credits on the
/// meter, live subscriptions adopt the new terms — see
/// <c>ISubscriptionRepository.AdoptMeterTopUpAsync</c>.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class MeterTopUpTerms
{
    /// <summary>
    /// Whether credits may be bought. Kept apart from the rest so an author can pause sales without
    /// losing the prices they configured.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>One price per currency. A purchase is only offered in the subscription's own.</summary>
    public List<MeterTopUpPrice> UnitPrices { get; set; } = [];

    /// <summary>The fewest whole credits one purchase may buy. At least one.</summary>
    public decimal MinQuantity { get; set; } = 1;

    /// <summary>
    /// The most whole credits one purchase may buy. Required, because it is what bounds a single
    /// charge to the subscriber's card.
    /// </summary>
    public decimal MaxQuantity { get; set; }

    /// <summary>
    /// When bought credits stop being usable. Reuses the meter's own vocabulary, read for a grant:
    /// <see cref="MeterResetPolicy.Periodic"/> expires at the end of the usage window it was bought
    /// in, <see cref="MeterResetPolicy.CarryForward"/> rolls across renewals until a plan change,
    /// and <see cref="MeterResetPolicy.Never"/> lasts as long as the subscription and its meter do.
    /// </summary>
    /// <remarks>
    /// Independent of <see cref="PlanMeter.ResetPolicy"/>: the plan's own allowance commonly resets
    /// every month while what a subscriber paid for separately does not. Nor does
    /// <see cref="PlanMeter.CarryForwardCap"/> apply here — that cap bounds what the plan's own
    /// allowance may bank, not what someone bought.
    /// </remarks>
    public MeterResetPolicy ExpiryPolicy { get; set; } = MeterResetPolicy.Periodic;

    /// <summary>Basis points out of 10,000. Null or zero is untaxed.</summary>
    public int? TaxRateBasisPoints { get; set; }

    /// <summary>Whether the rate is added to the price or already inside it. Required with a rate.</summary>
    public TaxMode? TaxMode { get; set; }

    /// <summary>The price of one credit in <paramref name="currencyCode"/>, or null when not sold in it.</summary>
    public MeterTopUpPrice? PriceIn(string currencyCode) =>
        UnitPrices.Find(price =>
            string.Equals(price.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A deep copy. Plans and subscription snapshots must never share one instance: a catalogue edit
    /// mutating it would reprice every subscriber holding the same reference.
    /// </summary>
    public MeterTopUpTerms Clone() => new()
    {
        Enabled = Enabled,
        UnitPrices = [.. UnitPrices.Select(price => new MeterTopUpPrice
        {
            CurrencyCode = price.CurrencyCode,
            UnitAmountMinor = price.UnitAmountMinor
        })],
        MinQuantity = MinQuantity,
        MaxQuantity = MaxQuantity,
        ExpiryPolicy = ExpiryPolicy,
        TaxRateBasisPoints = TaxRateBasisPoints,
        TaxMode = TaxMode
    };
}

/// <summary>What one credit costs in one currency, in minor units.</summary>
[BsonIgnoreExtraElements]
public sealed class MeterTopUpPrice
{
    public string CurrencyCode { get; set; } = string.Empty;

    public long UnitAmountMinor { get; set; }
}
