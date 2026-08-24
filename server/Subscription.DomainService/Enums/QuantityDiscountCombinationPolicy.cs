namespace Subscription.DomainService.Enums;

/// <summary>
/// What happens when a subscriber holds both a volume band and a promotional code.
/// </summary>
/// <remarks>
/// Stated on the plan rather than decided in the calculator, because it is a commercial choice
/// and not an arithmetic one. Left implicit, the answer becomes whichever order the code happens
/// to apply them in, which is how a promotion quietly ends up compounding a volume discount
/// nobody meant to give away.
/// </remarks>
public enum QuantityDiscountCombinationPolicy
{
    /// <summary>
    /// Whichever reduction is larger, and only that one. The default, and the safe answer.
    /// </summary>
    /// <remarks>
    /// Default because it is the only value that leaves a plan without bands calculating exactly
    /// as it did before: with no band there is nothing to compare, so the promotion wins and the
    /// arithmetic is unchanged.
    /// <para>
    /// A promotion that loses to the band is not consumed — it has reduced nothing, so it must not
    /// count against <see cref="Entities.DiscountTerms.DurationPeriods"/>. Three months of "20% off"
    /// spent losing to a volume band would otherwise expire without the customer ever seeing it.
    /// </para>
    /// </remarks>
    BestDiscount = 0,

    /// <summary>The band applies and the promotional code is ignored.</summary>
    QuantityOnly = 1,

    /// <summary>The band applies first, then the promotion reduces what is left.</summary>
    /// <remarks>Compounds. Worth choosing deliberately, which is why it is not the default.</remarks>
    Stack = 2
}
