using System.Globalization;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// Names the short window a sub-limit counts within, so its counter can be addressed directly.
/// </summary>
/// <remarks>
/// Deliberately a different keyspace from <see cref="PeriodKey"/>. A meter billed daily with a
/// daily sub-limit would otherwise name both windows identically and silently count them into one
/// counter — a sub-limit that enforced nothing because it shared the period's own balance.
/// <para>
/// The lowercase code is what keeps them apart, and it keeps them apart by construction rather than
/// by a rule somebody has to remember: no period key can ever equal a window key, whatever the plan
/// says.
/// </para>
/// </remarks>
public static class UsageWindowKey
{
    private const string InstantFormat = "yyyyMMdd'T'HHmmss";

    /// <summary>
    /// A fixed instant every multi-window block is tiled from, so two subscriptions authored
    /// years apart still land on the same block boundaries. Its own value carries no meaning
    /// beyond being fixed and being a Monday — the Monday is what keeps a multi-week block
    /// aligned with <see cref="StartOfWeek"/>, which every single-week plan already assumes.
    /// </summary>
    private static readonly DateTime BlockEpoch = new(1969, 12, 29, 0, 0, 0, DateTimeKind.Utc);

    public static string Create(UsageWindow window, DateTime instantUtc) =>
        Create(window, instantUtc, count: 1);

    /// <summary>
    /// The counter one fixed pace spends against, for a window that may span more than one unit.
    /// </summary>
    /// <remarks>
    /// <paramref name="count"/> of 1 is byte-identical to <see cref="Create(UsageWindow, DateTime)"/>
    /// — every meter authored before counted windows existed keeps the counter it has always had,
    /// with nothing to migrate.
    /// <para>
    /// Above one, the count is part of the key. A meter can carry several limits, and without it a
    /// fixed one-hour and a fixed two-hour limit would name the same counter at every even hour
    /// and spend each other's allowance.
    /// </para>
    /// </remarks>
    public static string Create(UsageWindow window, DateTime instantUtc, int count) =>
        string.Concat(
            Code(window),
            count > 1 ? string.Create(CultureInfo.InvariantCulture, $"{count}x") : string.Empty,
            StartOf(window, instantUtc, count).ToString(InstantFormat, CultureInfo.InvariantCulture),
            "Z");

    /// <summary>
    /// When the window containing this instant began.
    /// </summary>
    /// <remarks>
    /// A week starts on Monday, which is what ISO-8601 says and what a plan sold as "so much a
    /// week" is taken to mean. Truncated rather than offset from the subscription's anchor: a
    /// sub-limit is about pace, and a pace measured from each subscriber's own signup instant
    /// cannot be reasoned about by anybody looking at two of them.
    /// </remarks>
    public static DateTime StartOf(UsageWindow window, DateTime instantUtc) => window switch
    {
        UsageWindow.Hour => new DateTime(
            instantUtc.Year, instantUtc.Month, instantUtc.Day, instantUtc.Hour, 0, 0,
            DateTimeKind.Utc),
        UsageWindow.Day => new DateTime(
            instantUtc.Year, instantUtc.Month, instantUtc.Day, 0, 0, 0, DateTimeKind.Utc),
        UsageWindow.Week => StartOfWeek(instantUtc),
        _ => instantUtc
    };

    /// <summary>
    /// When the block of <paramref name="count"/> windows containing this instant began.
    /// </summary>
    /// <remarks>
    /// An hour block resets at midnight, because that is the reading validated at authoring time
    /// — <c>count</c> is refused unless it divides 24, precisely so this never leaves one short
    /// block a day. A day or week block has no such enclosing unit to divide, so it tiles instead
    /// from <see cref="BlockEpoch"/>: fixed, so every subscription's blocks land on the same
    /// boundaries, and forever consistent because nothing about it ever needs to be evenly
    /// divided.
    /// </remarks>
    public static DateTime StartOf(UsageWindow window, DateTime instantUtc, int count)
    {
        if (count <= 1)
        {
            return StartOf(window, instantUtc);
        }

        return window switch
        {
            UsageWindow.Hour => StartOfHourBlock(instantUtc, count),
            UsageWindow.Day => StartOfUnitBlock(
                instantUtc.Date, BlockEpoch.Date, TimeSpan.FromDays(1), count),
            UsageWindow.Week => StartOfUnitBlock(
                StartOfWeek(instantUtc), BlockEpoch, TimeSpan.FromDays(7), count),
            _ => instantUtc
        };
    }

    public static DateTime EndOf(UsageWindow window, DateTime instantUtc) => window switch
    {
        UsageWindow.Hour => StartOf(window, instantUtc).AddHours(1),
        UsageWindow.Day => StartOf(window, instantUtc).AddDays(1),
        UsageWindow.Week => StartOf(window, instantUtc).AddDays(7),
        _ => instantUtc
    };

    public static DateTime EndOf(UsageWindow window, DateTime instantUtc, int count)
    {
        if (count <= 1)
        {
            return EndOf(window, instantUtc);
        }

        return window switch
        {
            UsageWindow.Hour => StartOf(window, instantUtc, count).AddHours(count),
            UsageWindow.Day => StartOf(window, instantUtc, count).AddDays(count),
            UsageWindow.Week => StartOf(window, instantUtc, count).AddDays(7L * count),
            _ => instantUtc
        };
    }

    private static DateTime StartOfHourBlock(DateTime instantUtc, int count)
    {
        var blockStartHour = instantUtc.Hour / count * count;

        return new DateTime(
            instantUtc.Year, instantUtc.Month, instantUtc.Day, blockStartHour, 0, 0,
            DateTimeKind.Utc);
    }

    /// <summary>
    /// Floors <paramref name="unitStart"/> onto the nearest multiple of <paramref name="count"/>
    /// units counted from <paramref name="epoch"/>.
    /// </summary>
    private static DateTime StartOfUnitBlock(
        DateTime unitStart, DateTime epoch, TimeSpan unit, int count)
    {
        var unitsSinceEpoch = (long)((unitStart - epoch) / unit);
        var blockIndex = unitsSinceEpoch / count;

        return epoch + (unit * (blockIndex * count));
    }

    private static DateTime StartOfWeek(DateTime instantUtc)
    {
        var day = new DateTime(
            instantUtc.Year, instantUtc.Month, instantUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        // Sunday is 0 in DayOfWeek and six days into an ISO week, which is the one value the
        // arithmetic below would otherwise get wrong.
        var since = day.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)day.DayOfWeek - 1;

        return day.AddDays(-since);
    }

    private static char Code(UsageWindow window) => window switch
    {
        UsageWindow.Hour => 'h',
        UsageWindow.Day => 'd',
        UsageWindow.Week => 'w',
        _ => 'u'
    };
}
