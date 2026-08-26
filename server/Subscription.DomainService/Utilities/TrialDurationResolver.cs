using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// Turns an authored trial duration into the UTC instant it ends at.
/// </summary>
/// <remarks>
/// Called once, at subscription creation, and the result frozen onto
/// <see cref="Entities.TrialTerms"/> — a later catalogue edit must never move an existing
/// subscriber's trial boundary, so nothing here is ever re-evaluated against the plan again.
/// </remarks>
public static class TrialDurationResolver
{
    public static DateTime ResolveEndUtc(
        DateTime startUtc,
        TimeZoneInfo timeZone,
        TrialDurationKind kind,
        int? count)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return kind switch
        {
            // No time zone involved on purpose: a day-based trial is a fixed span, not a
            // calendar boundary, and has always ended exactly count × 24 hours after signup.
            TrialDurationKind.Days => startUtc.AddDays(count ?? 0),
            TrialDurationKind.EndOfCalendarMonth => EndOfCalendarMonthUtc(startUtc, timeZone),
            TrialDurationKind.AnniversaryMonths => AnniversaryMonthsUtc(startUtc, timeZone, count ?? 1),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unrecognized trial duration kind.")
        };
    }

    /// <summary>Local midnight on the first day of the month after the one signup fell in.</summary>
    private static DateTime EndOfCalendarMonthUtc(DateTime startUtc, TimeZoneInfo timeZone)
    {
        var startLocal = BillingLocalTime.ToLocal(startUtc, timeZone);
        var firstOfNextMonth = new DateTime(startLocal.Year, startLocal.Month, 1).AddMonths(1);

        return BillingLocalTime.ToUtc(firstOfNextMonth, timeZone);
    }

    /// <summary>
    /// The same local wall-clock time, <paramref name="months"/> later, clamped to the target
    /// month's last day when the signup day does not exist there.
    /// </summary>
    private static DateTime AnniversaryMonthsUtc(DateTime startUtc, TimeZoneInfo timeZone, int months)
    {
        var startLocal = BillingLocalTime.ToLocal(startUtc, timeZone);
        var targetMonth = new DateTime(startLocal.Year, startLocal.Month, 1).AddMonths(months);
        var day = Math.Min(startLocal.Day, DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month));

        var targetLocal = new DateTime(
            targetMonth.Year, targetMonth.Month, day,
            startLocal.Hour, startLocal.Minute, startLocal.Second, startLocal.Millisecond);

        return BillingLocalTime.ToUtc(targetLocal, timeZone);
    }
}
