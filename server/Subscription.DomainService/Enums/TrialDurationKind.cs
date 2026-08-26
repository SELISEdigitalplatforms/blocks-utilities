using System.Text.Json.Serialization;

namespace Subscription.DomainService.Enums;

/// <summary>
/// How a trial's length is measured. <see cref="Days"/> is the default (value 0) so every plan
/// authored before this existed — which only ever set the legacy <c>TrialDays</c> field — keeps
/// meaning exactly what it always meant.
/// </summary>
/// <remarks>
/// Explicit string conversion: this is bound straight off a plan-authoring request body, where
/// System.Text.Json's default numeric enum handling would reject the string names a caller
/// actually sends — see the remark on
/// <see cref="Subscription.DomainService.Simulation.SimulatedRenewalOutcome"/> for the identical
/// gap this closes the same way.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrialDurationKind
{
    /// <summary>A fixed number of days from signup — <c>count × 24 hours</c>, no time zone involved.</summary>
    Days = 0,

    /// <summary>Ends at local midnight on the first day of the month after signup. No count.</summary>
    EndOfCalendarMonth = 1,

    /// <summary>
    /// Ends the given number of months after signup, same local wall-clock time, clamped to the
    /// target month's last day when the signup date does not exist there (e.g. Jan 31 + 1 month).
    /// </summary>
    AnniversaryMonths = 2
}
