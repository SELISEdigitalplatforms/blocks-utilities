using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A fixed lifetime for bought or granted credits, counted from the moment they are issued:
/// "30 days", "6 months", "1 year".
/// </summary>
/// <remarks>
/// Independent of <see cref="MeterTopUpTerms.ExpiryPolicy"/>, which ties credits to the usage
/// window or the plan. An author who wants neither — credits that simply last a set time whatever
/// the billing calendar does — sets this instead. When both apply, a grant ends at whichever comes
/// first, so a lifetime can also cap a window-bound or carried-forward grant.
/// <para>
/// Months and years are calendar arithmetic, not a count of days: 1 month from 31 January is
/// 28 February, the same rule a billing period follows.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class CreditExpiryDuration
{
    /// <summary>How many of <see cref="Unit"/>. At least one.</summary>
    public int Count { get; set; }

    public CreditExpiryUnit Unit { get; set; } = CreditExpiryUnit.Day;

    /// <summary>The instant credits issued at <paramref name="fromUtc"/> stop being usable.</summary>
    public DateTime EndFrom(DateTime fromUtc) => Unit switch
    {
        CreditExpiryUnit.Hour => fromUtc.AddHours(Count),
        CreditExpiryUnit.Day => fromUtc.AddDays(Count),
        CreditExpiryUnit.Week => fromUtc.AddDays(7L * Count),
        CreditExpiryUnit.Month => fromUtc.AddMonths(Count),
        CreditExpiryUnit.Year => fromUtc.AddYears(Count),
        _ => throw new InvalidOperationException($"Unknown credit expiry unit {Unit}.")
    };

    public CreditExpiryDuration Clone() => new() { Count = Count, Unit = Unit };
}
