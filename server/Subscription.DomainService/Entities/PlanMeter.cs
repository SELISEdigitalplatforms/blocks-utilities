using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A named thing the plan counts.
/// </summary>
/// <remarks>
/// The key is the product's word — "screening" for one client, "envelope" for another — and the
/// platform treats it as an opaque name throughout. Nothing here knows what is being counted,
/// which is what lets a second product be onboarded as configuration.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PlanMeter
{
    public string MeterKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string UnitLabel { get; set; } = string.Empty;

    public MeterAggregation Aggregation { get; set; } = MeterAggregation.Sum;

    /// <summary>Periodic by default; Never keeps one lifetime balance across renewals.</summary>
    public MeterResetPolicy ResetPolicy { get; set; } = MeterResetPolicy.Periodic;

    /// <summary>
    /// How many decimal places this meter's quantities may carry.
    /// </summary>
    /// <remarks>
    /// Zero — whole units only — unless the plan author raises it, which is what keeps a meter
    /// counting screenings from accepting half of one. A meter authored before fractions existed has
    /// no such field, so it deserializes to zero and cannot behave differently from before.
    /// <para>
    /// It governs the allowance and the cap here, this meter's tier bounds, a trial grant for it, and
    /// every quantity recorded against it. Bounded by
    /// <see cref="Utilities.MeterQuantity.MaxScale"/>.
    /// </para>
    /// </remarks>
    public int QuantityScale { get; set; }

    /// <summary>How much the plan includes per period, or for its lifetime when reset is Never.</summary>
    public decimal IncludedQuantity { get; set; }

    /// <summary>
    /// The most that may roll into one window under
    /// <see cref="MeterResetPolicy.CarryForward"/>. Required for that policy, null for the others.
    /// </summary>
    /// <remarks>
    /// Bounds the amount carried in, not the total, so the plan's own included quantity is always
    /// available on top of it. Required because an unbounded roll is almost never what was sold: a
    /// subscription that consumes nothing for a year would otherwise arrive at month thirteen
    /// holding twelve months of quota.
    /// </remarks>
    public decimal? CarryForwardCap { get; set; }

    /// <summary>Whether usage past the included quantity is permitted and billed.</summary>
    public bool OverageAllowed { get; set; } = true;

    /// <summary>
    /// A shorter window this meter also caps within, or null when only the period's allowance
    /// applies.
    /// </summary>
    /// <remarks>
    /// The pace a plan is sold at, as distinct from the amount. Ten million tokens a month with no
    /// shorter cap can be spent in an afternoon, and a plan priced on the assumption they would not
    /// be has no way to say so.
    /// <para>
    /// Null on every meter authored before this, which is every meter today: they cap by period
    /// alone and go on doing exactly that.
    /// </para>
    /// </remarks>
    public UsageWindow? SubLimitWindow { get; set; }

    /// <summary>
    /// How many of <see cref="SubLimitWindow"/> the limit spans. One unless the plan says otherwise.
    /// </summary>
    /// <remarks>
    /// Anything above one is measured rolling, and has to be: a fixed window only tiles a day
    /// evenly when its length divides one, so "every five hours" from midnight would leave a short
    /// block straddling into the next day and no two days alike. See <see cref="SubLimitRolling"/>.
    /// </remarks>
    public int SubLimitWindowCount { get; set; } = 1;

    /// <summary>
    /// Whether the limit looks back from now rather than counting within a window on the clock.
    /// </summary>
    /// <remarks>
    /// False on every meter authored before this existed, which is what keeps them counting exactly
    /// as they did: the hour on the clock, the day, the week from Monday.
    /// <para>
    /// Rolling is the stricter reading and usually the intended one. A clock-aligned hourly cap
    /// lets a whole hour's worth be spent at 09:59 and the next at 10:01; a rolling one does not,
    /// because the first is still inside the span the second is measured against.
    /// </para>
    /// </remarks>
    public bool SubLimitRolling { get; set; }

    /// <summary>How much may be used within one <see cref="SubLimitWindow"/>.</summary>
    public decimal? SubLimitQuantity { get; set; }

    /// <summary>
    /// What happens on reaching <see cref="SubLimitQuantity"/> while the period still has
    /// allowance left.
    /// </summary>
    /// <remarks>
    /// Refusing is the default because it is the safe reading of a cap: a plan that says "so much
    /// an hour" and then allows more has not capped anything. A plan author who wants the softer
    /// behaviour says so.
    /// </remarks>
    public MeterSubLimitBehaviour SubLimitBehaviour { get; set; } = MeterSubLimitBehaviour.Refuse;

    /// <summary>
    /// Percentages of the included quantity that raise an event when first crossed, such as
    /// 80 and 100. Each fires once per period.
    /// </summary>
    public List<int> ThresholdPercents { get; set; } = [];

    public List<MeterRateTable> RateTables { get; set; } = [];
}
