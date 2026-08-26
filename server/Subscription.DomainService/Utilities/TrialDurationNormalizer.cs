using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// Reconciles a stored plan's trial fields into one answer, so a plan authored before
/// <see cref="TrialDurationKind"/> existed — which only ever set the legacy <c>TrialDays</c> —
/// keeps behaving exactly as it always did.
/// </summary>
/// <remarks>
/// A stored <see cref="Plan.TrialDurationKind"/> of <see cref="TrialDurationKind.Days"/> is
/// ambiguous by itself: it is both the explicit new-style choice and the default every
/// pre-existing document deserializes to. The two are told apart the same way either way —
/// falling back to <see cref="Plan.TrialDays"/> when the new count was never set — so this is
/// the single place that reconciliation happens, used by both response mapping and subscription
/// creation.
/// </remarks>
public static class TrialDurationNormalizer
{
    /// <summary>
    /// The count <see cref="Plan.TrialDurationKind"/> measures — a day count for
    /// <see cref="TrialDurationKind.Days"/>, a month count for
    /// <see cref="TrialDurationKind.AnniversaryMonths"/>, or null for
    /// <see cref="TrialDurationKind.EndOfCalendarMonth"/> or a plan with no trial at all.
    /// </summary>
    public static int? EffectiveCount(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.TrialDurationKind == TrialDurationKind.Days
            ? plan.TrialDurationCount ?? plan.TrialDays
            : plan.TrialDurationCount;
    }

    /// <summary>Whether this plan configures a trial at all.</summary>
    public static bool HasTrial(Plan plan) =>
        plan.TrialDurationKind != TrialDurationKind.Days || EffectiveCount(plan) is not null;
}
