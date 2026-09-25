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

    public static string Create(UsageWindow window, DateTime instantUtc) =>
        string.Concat(
            Code(window),
            StartOf(window, instantUtc).ToString(InstantFormat, CultureInfo.InvariantCulture),
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

    public static DateTime EndOf(UsageWindow window, DateTime instantUtc) => window switch
    {
        UsageWindow.Hour => StartOf(window, instantUtc).AddHours(1),
        UsageWindow.Day => StartOf(window, instantUtc).AddDays(1),
        UsageWindow.Week => StartOf(window, instantUtc).AddDays(7),
        _ => instantUtc
    };

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
