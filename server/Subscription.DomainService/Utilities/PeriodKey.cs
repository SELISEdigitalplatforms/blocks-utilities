using System.Globalization;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// Names a usage period so a counter can be addressed without looking one up.
/// </summary>
/// <remarks>
/// Built from the period's own start instant rather than from a calendar month, because a
/// cadence of more than one month would otherwise give two different periods the same name and
/// silently merge their counters.
/// </remarks>
public static class PeriodKey
{
    private const string InstantFormat = "yyyyMMdd'T'HHmmss";

    public static string Create(BillingInterval interval, DateTime periodStartUtc) =>
        string.Concat(
            Code(interval),
            periodStartUtc.ToString(InstantFormat, CultureInfo.InvariantCulture),
            "Z");

    /// <summary>
    /// Reads back the instant a period key names.
    /// </summary>
    /// <remarks>
    /// The key is the only record of which period a charge covered that survives on the payment
    /// itself, so a document issued after the subscription has moved on can still state the right
    /// service period rather than the one the subscriber happens to be in now. That gap is normally
    /// seconds and occasionally — after an outage, or a renewal that caught up several periods at
    /// once — much longer.
    /// <para>
    /// Deliberately tolerant of an unrecognised shape rather than throwing. A key it cannot read is
    /// a period it cannot name, which costs a less precise document; refusing to issue one at all
    /// would cost the invoice.
    /// </para>
    /// </remarks>
    public static bool TryDecodeStart(string? periodKey, out DateTime startUtc)
    {
        startUtc = default;

        // One interval letter, the instant, and the Z: anything else was written by something else.
        if (periodKey is not { Length: 17 } key || key[^1] != 'Z')
        {
            return false;
        }

        if (!DateTime.TryParseExact(
                key[1..^1],
                InstantFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        startUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);

        return true;
    }

    private static string Code(BillingInterval interval) => interval switch
    {
        BillingInterval.Day => "D",
        BillingInterval.Week => "W",
        BillingInterval.Month => "M",
        BillingInterval.Year => "Y",
        _ => "U"
    };
}
