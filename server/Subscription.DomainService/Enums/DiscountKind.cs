namespace Subscription.DomainService.Enums;

/// <summary>
/// How a discount reduces a charge.
/// </summary>
public enum DiscountKind
{
    /// <summary>A proportion, held in basis points so a third off is exact.</summary>
    Percent = 0,

    /// <summary>A fixed amount in the subscription's own currency.</summary>
    FixedAmount = 1
}
