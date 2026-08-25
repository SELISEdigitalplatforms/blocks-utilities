using System.Text.Json.Serialization;

namespace Subscription.DomainService.Enums;

/// <summary>
/// What happens when a price's own automatic discount meets the quantity band a subscriber's
/// volume selects.
/// </summary>
/// <remarks>
/// Stated on the price rather than decided in the calculator, for the same reason
/// <see cref="QuantityDiscountCombinationPolicy"/> is stated on the plan: it is a commercial choice,
/// and left implicit the answer becomes whichever order the arithmetic happens to run in. The two
/// are different questions about different pairs — this one is "8% for paying yearly, plus 5% for
/// buying fifty seats"; that one is "and what about the coupon they typed".
/// <para>
/// <see cref="BestDiscount"/> is zero so a price authored before this existed, or one sent by a
/// caller that has never heard of it, reads back as the conservative answer: whichever single
/// reduction is larger, never both.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AutomaticDiscountCombination
{
    /// <summary>
    /// The larger of the two reductions, and only that one. The default, and the safe answer.
    /// </summary>
    /// <remarks>
    /// Compared as money rather than as basis points, because the two rates are not always applied
    /// to the same base — and it is the money the subscriber notices.
    /// </remarks>
    BestDiscount = 0,

    /// <summary>
    /// Both rates, added together and applied once to the gross amount. 8% for the year plus 5% for
    /// the volume is 13% off, capped at 100%.
    /// </summary>
    /// <remarks>
    /// Added rather than compounded: 8% then 5% of what is left is 12.6%, which is not the number
    /// anybody wrote on the pricing page. Capped because two generous bands can otherwise sum past
    /// everything, and a charge must never arrive negative.
    /// </remarks>
    Additive = 1
}
