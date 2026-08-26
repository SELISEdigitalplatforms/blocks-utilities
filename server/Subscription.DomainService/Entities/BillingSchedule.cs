using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A recurring cadence, expressed so every future boundary can be recomputed rather than
/// remembered.
/// </summary>
/// <remarks>
/// <see cref="AnchorDayOfMonth"/> holds the day the subscriber actually chose and is never
/// overwritten with a clamped one. A subscription anchored on the 31st bills on the 28th in
/// February and returns to the 31st in March; persisting February's clamp would quietly drag
/// every later period earlier for the life of the subscription.
/// <para>
/// The time zone is stored per subscription because "the 1st" has no meaning without one, and
/// a server's own zone is not the customer's.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class BillingSchedule
{
    public BillingInterval Interval { get; set; } = BillingInterval.Month;

    public int IntervalCount { get; set; } = 1;

    /// <summary>The instant the cadence starts counting from.</summary>
    public DateTime AnchorInstantUtc { get; set; }

    /// <summary>An IANA identifier such as <c>Europe/Zurich</c>.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>The originally chosen day, 1 to 31. Clamped on read, never on write.</summary>
    public int AnchorDayOfMonth { get; set; } = 1;

    /// <summary>Local time of day for a boundary, as minutes from midnight.</summary>
    public int AnchorMinutesFromMidnight { get; set; }
}
