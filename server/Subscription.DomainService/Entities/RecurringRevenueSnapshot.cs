using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The tenant's recurring run-rate as it stood on one UTC day.
/// </summary>
/// <remarks>
/// Captured because it cannot be reconstructed. The live report reads each subscription's current
/// price, quantities and discount, and none of the history the module keeps records all three
/// together, so last month's run-rate is gone the moment a subscription changes. A capture taken
/// with the same calculation the live report uses is the only way a trend and today's figure can
/// agree.
/// <para>
/// Keyed by tenant and day, so a second capture on the same day replaces the first rather than
/// adding a row. The tenant is in the key even though each tenant has its own database, for the
/// same reason every query in this module filters on it: a key that relied on the database
/// boundary would let one tenant's capture overwrite another's wherever that boundary is absent.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class RecurringRevenueSnapshot
{
    /// <summary><c>{tenantId}:{yyyy-MM-dd}</c>. See <see cref="KeyFor"/>.</summary>
    [BsonId]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Midnight UTC of the day captured.</summary>
    public DateTime DayUtc { get; set; }

    public string TenantId { get; set; } = string.Empty;

    /// <summary>When the figures were actually read, which is some time after midnight.</summary>
    public DateTime CapturedAtUtc { get; set; }

    public long SubscriptionCount { get; set; }

    public List<RecurringRevenueSnapshotCurrency> Currencies { get; set; } = [];

    public static string KeyFor(string tenantId, DateTime dayUtc) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{tenantId}:{dayUtc:yyyy-MM-dd}");
}

/// <summary>One currency's run-rate within a capture. Never summed across currencies.</summary>
[BsonIgnoreExtraElements]
public sealed class RecurringRevenueSnapshotCurrency
{
    public string CurrencyCode { get; set; } = string.Empty;

    public long GrossMonthlyMinor { get; set; }

    public long NetMonthlyMinor { get; set; }

    public long SubscriptionCount { get; set; }
}
