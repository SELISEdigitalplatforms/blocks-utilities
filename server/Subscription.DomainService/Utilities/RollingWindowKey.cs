using System.Globalization;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// Addresses a rolling sub-limit: the one counter it spends against, and the minute buckets the
/// spending is split into so that "the last five hours" can be summed without reading the ledger.
/// </summary>
/// <remarks>
/// A rolling window has no start of its own — it ends now and begins a span before now — so unlike
/// <see cref="UsageWindowKey"/> there is nothing in the instant to name a counter after. The
/// counter is named for the <em>rule</em> instead, and holds every bucket the rule has ever seen.
/// <para>
/// Bucketing is what keeps this an enforcement point. Summing the ledger over a moving span is a
/// read, so two callers arriving together would both see a sum under the cap and both be allowed
/// through. A bucket is incremented atomically exactly as the period's own balance is, and the
/// document that comes back already includes the caller's own use — so only one of two callers at
/// the boundary is over it.
/// </para>
/// </remarks>
public static class RollingWindowKey
{
    /// <summary>
    /// The finest span a rolling limit distinguishes. A use is counted into the minute it happened
    /// in, so a window can be out by up to this much at its trailing edge.
    /// </summary>
    /// <remarks>
    /// A minute rather than a second because the bucket names live in one document and a second's
    /// granularity would put 18,000 of them in a five-hour window for accuracy nobody is buying a
    /// plan on. An hour would be too coarse the other way: a five-hour rule kept in hour buckets
    /// would forgive a fifth of itself at once.
    /// </remarks>
    public static readonly TimeSpan BucketSize = TimeSpan.FromMinutes(1);

    private const string BucketFormat = "yyyyMMddHHmm";

    /// <summary>
    /// The counter a rolling rule spends against, named for the rule rather than for any instant.
    /// </summary>
    /// <remarks>
    /// The span is in the name on purpose. An author who changes five hours to six is describing a
    /// different rule, and carrying the old rule's spending into it would enforce a limit against
    /// usage that was never measured for it.
    /// <para>
    /// Lowercase <c>r</c> for the same reason every other window code is lowercase: it cannot
    /// collide with a <c>PeriodKey</c>, whose codes are uppercase, whatever the plan says.
    /// </para>
    /// </remarks>
    public static string Create(UsageWindow window, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"r{count}{char.ToLowerInvariant(Unit(window))}");

    /// <summary>The bucket a use at this instant is counted into.</summary>
    public static string BucketFor(DateTime instantUtc) =>
        StartOfBucket(instantUtc).ToString(BucketFormat, CultureInfo.InvariantCulture);

    /// <summary>How far back a rolling rule looks.</summary>
    public static TimeSpan SpanOf(UsageWindow window, int count) =>
        window switch
        {
            UsageWindow.Hour => TimeSpan.FromHours(count),
            UsageWindow.Day => TimeSpan.FromDays(count),
            UsageWindow.Week => TimeSpan.FromDays(7 * (long)count),
            _ => TimeSpan.FromHours(count)
        };

    /// <summary>
    /// What the buckets say has been spent in the span ending at <paramref name="nowUtc"/>.
    /// </summary>
    /// <remarks>
    /// The bucket a use landed in is included whenever any part of it falls inside the span, which
    /// is the forgiving direction by design: the alternative drops a use that happened seconds ago
    /// because its minute started before the cutoff, and refusing somebody is the outcome that must
    /// not happen by accident.
    /// <para>
    /// A bucket whose name is not a bucket name is ignored rather than throwing. Nothing else writes
    /// to this map, but a limit is not the place to fail a request over a stray key.
    /// </para>
    /// </remarks>
    public static decimal SumWithin(
        IReadOnlyDictionary<string, decimal>? buckets,
        TimeSpan span,
        DateTime nowUtc)
    {
        if (buckets is null || buckets.Count == 0)
        {
            return 0;
        }

        var cutoff = StartOfBucket(nowUtc - span);
        var total = 0m;

        foreach (var (name, quantity) in buckets)
        {
            if (TryParseBucket(name, out var start) && start >= cutoff)
            {
                total += quantity;
            }
        }

        return total;
    }

    /// <summary>
    /// The bucket names that can no longer affect any answer and may be dropped.
    /// </summary>
    /// <remarks>
    /// Kept for a second span beyond the one being measured, so that a use recorded slightly out of
    /// order — a queued call, a clock a few seconds behind — still lands in a bucket that exists
    /// rather than reviving one that was just pruned.
    /// </remarks>
    public static IReadOnlyList<string> Expired(
        IReadOnlyDictionary<string, decimal>? buckets,
        TimeSpan span,
        DateTime nowUtc)
    {
        if (buckets is null || buckets.Count == 0)
        {
            return [];
        }

        var cutoff = StartOfBucket(nowUtc - span - span);
        var expired = new List<string>();

        foreach (var name in buckets.Keys)
        {
            if (!TryParseBucket(name, out var start) || start < cutoff)
            {
                expired.Add(name);
            }
        }

        return expired;
    }

    private static DateTime StartOfBucket(DateTime instantUtc) =>
        new(
            instantUtc.Year,
            instantUtc.Month,
            instantUtc.Day,
            instantUtc.Hour,
            instantUtc.Minute,
            0,
            DateTimeKind.Utc);

    private static bool TryParseBucket(string name, out DateTime startUtc) =>
        DateTime.TryParseExact(
            name,
            BucketFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out startUtc);

    private static char Unit(UsageWindow window) =>
        window switch
        {
            UsageWindow.Hour => 'h',
            UsageWindow.Day => 'd',
            UsageWindow.Week => 'w',
            _ => 'u'
        };
}
