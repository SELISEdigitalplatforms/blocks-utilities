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

    /// <summary>
    /// Whether <see cref="Firms"/> was recorded. False on every capture taken before it was.
    /// </summary>
    /// <remarks>
    /// A flag rather than "the list is empty", because an empty list is also what a tenant with no
    /// paying firms captures. Without it, comparing an older capture to a newer one would report
    /// every firm as new, or every firm as lost.
    /// </remarks>
    public bool CapturedFirms { get; set; }

    /// <summary>
    /// Each firm's run-rate on the day, one row per organization and currency.
    /// </summary>
    /// <remarks>
    /// What lets two captures be compared firm by firm, which is the only way to split a change in
    /// run-rate into new, expansion, contraction and churn. Embedded in the day's document rather
    /// than stored as rows of its own, so a capture is still one write. The ceiling is the 16 MB
    /// document limit, around a hundred thousand firms; past that the rows move to a collection
    /// keyed by day.
    /// </remarks>
    public List<RecurringRevenueSnapshotFirm> Firms { get; set; } = [];

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

/// <summary>One organization's run-rate in one currency within a capture.</summary>
[BsonIgnoreExtraElements]
public sealed class RecurringRevenueSnapshotFirm
{
    public string OrganizationId { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = string.Empty;

    public long NetMonthlyMinor { get; set; }

    public long SubscriptionCount { get; set; }
}
