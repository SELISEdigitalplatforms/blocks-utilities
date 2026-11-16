using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The terms on which an organization may buy a meter's credits outright, with or without a
/// subscription. See spec 005.
/// </summary>
/// <remarks>
/// Separate from <see cref="MeterTopUpTerms"/>, which prices credits for one plan's subscribers
/// and is snapshotted onto their subscriptions. An offer is read live: a purchase is quoted from
/// the offer as it stands and records the <see cref="Version"/> it was quoted from. The expiry is
/// copied onto the grant at purchase, so a later edit never shortens or extends what was bought;
/// <see cref="SpendOrder"/> is read at usage time, so flipping it changes consumption at once.
/// <para>
/// One per meter per scope. <see cref="OrganizationId"/> resolves the way a plan's does: a buying
/// organization's own offer wins, otherwise the tenant's, which is how a seller gives one customer
/// a special price.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class CreditOffer
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Null is the tenant-wide offer.</summary>
    public string? OrganizationId { get; set; }

    /// <summary>A catalogue meter resolvable in this scope. Never changes once created.</summary>
    public string MeterKey { get; set; } = string.Empty;

    /// <summary>Pause sales without losing the prices configured.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>One price per currency. The buyer picks which to pay in.</summary>
    public List<MeterTopUpPrice> UnitPrices { get; set; } = [];

    public decimal MinQuantity { get; set; } = 1;

    /// <summary>Required: it is what bounds a single charge.</summary>
    public decimal MaxQuantity { get; set; }

    /// <summary>A fixed lifetime from purchase, or null for credits that never expire.</summary>
    public CreditExpiryDuration? ExpiresAfter { get; set; }

    public int? TaxRateBasisPoints { get; set; }

    public TaxMode? TaxMode { get; set; }

    public CreditSpendOrder SpendOrder { get; set; } = CreditSpendOrder.SubscriptionFirst;

    /// <summary>
    /// Fixed-size bundles sold under this offer — a starter pack, a bulk pack — each with its own
    /// price per currency. Everything else about a pack purchase (expiry, tax, spend order) is the
    /// offer's. Empty for an offer that sells by the credit only.
    /// </summary>
    public List<CreditPack> Packs { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public DateTime LastUpdatedAtUtc { get; set; }

    /// <summary>Raised by every edit, and recorded on each purchase quoted from it.</summary>
    public long Version { get; set; }

    public MeterTopUpPrice? PriceIn(string currencyCode) =>
        UnitPrices.Find(price =>
            string.Equals(price.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase));

    public CreditPack? PackByCode(string code) =>
        Packs.Find(pack => string.Equals(pack.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The same terms in the shape the credit quote calculator prices, so an offer and a plan
    /// top-up are priced by one piece of arithmetic.
    /// </summary>
    public MeterTopUpTerms ToTopUpTerms() => new()
    {
        Enabled = Enabled,
        UnitPrices = [.. UnitPrices],
        MinQuantity = MinQuantity,
        MaxQuantity = MaxQuantity,
        ExpiryPolicy = MeterResetPolicy.Never,
        ExpiresAfter = ExpiresAfter?.Clone(),
        TaxRateBasisPoints = TaxRateBasisPoints,
        TaxMode = TaxMode
    };
}

/// <summary>
/// A fixed quantity of an offer's credits at its own unit price: how a seller makes a bigger
/// purchase cheaper per credit without a discount code. Bought by code; the quantity is the
/// pack's, not the buyer's, and the offer's purchase bounds do not apply to it.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class CreditPack
{
    /// <summary>Unique within the offer, case-insensitive. What a purchase names.</summary>
    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Whole credits.</summary>
    public decimal Quantity { get; set; }

    /// <summary>One price per currency, as the offer's. A currency the pack lacks cannot buy it.</summary>
    public List<MeterTopUpPrice> UnitPrices { get; set; } = [];

    public MeterTopUpPrice? PriceIn(string currencyCode) =>
        UnitPrices.Find(price =>
            string.Equals(price.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase));
}
