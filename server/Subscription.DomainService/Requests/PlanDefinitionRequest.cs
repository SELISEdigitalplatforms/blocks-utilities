namespace Subscription.DomainService.Requests;

using Subscription.DomainService.Enums;

/// <summary>
/// What a plan sells, shared by authoring one and editing one.
/// </summary>
/// <remarks>
/// Everything a plan is *made of* lives here; what it *is* — its code and the organization it is
/// scoped to — belongs to the request that creates it, because neither may change afterwards. A
/// code is what configuration points at, and a scope change would move a plan out from under the
/// organization that can see it.
/// </remarks>
public abstract class PlanDefinitionRequest
{
    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Arbitrary plan features as JSON. Stored verbatim and handed back untouched; nothing here
    /// interprets it, which is what lets a second product ship its own flags without a change
    /// to this module.
    /// </summary>
    public string? FeaturesJson { get; set; }

    /// <summary>
    /// Legacy day-count trial. Still accepted so existing callers keep working, but mutually
    /// exclusive with <see cref="TrialDurationKind"/> — a request naming both is rejected rather
    /// than guessing which one wins.
    /// </summary>
    public int? TrialDays { get; set; }

    /// <summary>
    /// How a trial's length is measured. Null (the default) with <see cref="TrialDays"/> also
    /// null means no trial. Prefer this over the legacy <see cref="TrialDays"/> for new plans.
    /// </summary>
    public TrialDurationKind? TrialDurationKind { get; set; }

    /// <summary>
    /// The count <see cref="TrialDurationKind"/> is measured in — required for
    /// <see cref="Enums.TrialDurationKind.Days"/> (1-365) and
    /// <see cref="Enums.TrialDurationKind.AnniversaryMonths"/> (1-12), and must be omitted for
    /// <see cref="Enums.TrialDurationKind.EndOfCalendarMonth"/>.
    /// </summary>
    public int? TrialDurationCount { get; set; }

    public bool TrialRequiresPaymentMethod { get; set; } = true;

    /// <summary>
    /// Require a card before activation even when nothing is due today. Omit for the historical
    /// behaviour, which is to start a zero-amount subscription immediately.
    /// </summary>
    public bool RequirePaymentMethodUpfront { get; set; }

    public BillingInterval UsageInterval { get; set; } = BillingInterval.Month;

    public int UsageIntervalCount { get; set; } = 1;

    public string? FamilyCode { get; set; }

    public int? FamilyRank { get; set; }

    /// <summary>How a volume band combines with a promotional code. Defaults to the larger of the two.</summary>
    public QuantityDiscountCombinationPolicy QuantityDiscountCombinationPolicy { get; set; } =
        QuantityDiscountCombinationPolicy.BestDiscount;

    public List<PlanQuantityItemRequest> QuantityItems { get; set; } = [];

    public List<PlanMeterRequest> Meters { get; set; } = [];

    public List<PlanEntitlementRequest> Entitlements { get; set; } = [];

    public List<TrialGrantRequest> TrialGrants { get; set; } = [];
}
