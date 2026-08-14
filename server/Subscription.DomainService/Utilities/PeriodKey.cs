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

    private static string Code(BillingInterval interval) => interval switch
    {
        BillingInterval.Day => "D",
        BillingInterval.Week => "W",
        BillingInterval.Month => "M",
        BillingInterval.Year => "Y",
        _ => "U"
    };
}
