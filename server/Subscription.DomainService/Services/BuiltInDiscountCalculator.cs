using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Services;

/// <summary>
/// The reduction a subscriber gets without typing anything: the price's own automatic discount and
/// the volume band their quantity selects, combined the way the price says to.
/// </summary>
/// <remarks>
/// Pure and static, like the other calculators here. Its own type rather than a branch inside
/// <see cref="SubscriptionAmountCalculator"/>, because "how much comes off before a coupon is even
/// considered" is one question with one answer, and every money path — signup, renewal, quantity
/// preview, proration, usage overage — has to get the same one.
/// <para>
/// A monthly price with no automatic discount and a plan with no bands resolves to no reduction and
/// prices exactly as it did before any of this existed. That is the case almost every stored price
/// is in, so it is the case that must be arithmetically untouched.
/// </para>
/// </remarks>
public static class BuiltInDiscountCalculator
{
    private const int FullBasisPoints = 10_000;

    /// <param name="grossAmountMinor">
    /// The undiscounted charge — <see cref="SubscriptionAmountCalculator.GrossAmountMinor"/>, passed
    /// in rather than read off <paramref name="band"/> so the caller keeps one gross for the whole
    /// calculation.
    /// </param>
    /// <param name="band">
    /// The volume band the quantity selects, already resolved. Its own reduction is used verbatim
    /// when it wins, so a plan that had bands before automatic discounts existed charges the same
    /// figure to the minor unit.
    /// </param>
    /// <param name="automaticBasisPoints">
    /// The price's automatic discount. Null or zero is no automatic discount, which is what every
    /// price authored before this existed carries.
    /// </param>
    /// <param name="combination">
    /// How the two meet. Null reads as <see cref="AutomaticDiscountCombination.BestDiscount"/> — the
    /// conservative answer, and the one a caller that omitted the field almost certainly meant.
    /// </param>
    public static BuiltInDiscount Resolve(
        long grossAmountMinor,
        QuantityDiscountOutcome band,
        int? automaticBasisPoints,
        AutomaticDiscountCombination? combination)
    {
        var automatic = Math.Clamp(automaticBasisPoints ?? 0, 0, FullBasisPoints);
        var bandBasisPoints = Math.Max(0, band.DiscountBasisPoints);
        var bandDiscount = Math.Max(0, band.DiscountAmountMinor);

        if (grossAmountMinor <= 0 || (automatic == 0 && bandDiscount == 0))
        {
            return new BuiltInDiscount(
                grossAmountMinor,
                automatic,
                bandBasisPoints,
                0,
                0,
                grossAmountMinor);
        }

        if (automatic == 0)
        {
            // The band alone, with its own arithmetic rather than a recomputation of it: a plan
            // that priced a band one way before this existed must not start rounding it another.
            return new BuiltInDiscount(
                grossAmountMinor,
                0,
                bandBasisPoints,
                bandBasisPoints,
                bandDiscount,
                Math.Max(0, grossAmountMinor - bandDiscount));
        }

        if ((combination ?? AutomaticDiscountCombination.BestDiscount) ==
            AutomaticDiscountCombination.Additive)
        {
            // One rate, applied once. Adding the rates and taking a single percentage is what an
            // author who wrote "8% + 5% = 13%" means; applying them in sequence gives 12.6% and no
            // way to explain the missing 0.4 on an invoice.
            var combined = Math.Min(FullBasisPoints, automatic + bandBasisPoints);
            var discount = DiscountOn(grossAmountMinor, combined);

            return new BuiltInDiscount(
                grossAmountMinor,
                automatic,
                bandBasisPoints,
                combined,
                discount,
                grossAmountMinor - discount);
        }

        var automaticDiscount = DiscountOn(grossAmountMinor, automatic);

        // Compared as money, and ties go to the automatic discount. Neither can be consumed the way
        // a promotional code can, so which one wins a tie changes nothing a subscriber can see —
        // but reporting the price's own rate keeps the breakdown consistent with what was authored.
        return bandDiscount > automaticDiscount
            ? new BuiltInDiscount(
                grossAmountMinor,
                automatic,
                bandBasisPoints,
                bandBasisPoints,
                bandDiscount,
                Math.Max(0, grossAmountMinor - bandDiscount))
            : new BuiltInDiscount(
                grossAmountMinor,
                automatic,
                bandBasisPoints,
                automatic,
                automaticDiscount,
                grossAmountMinor - automaticDiscount);
    }

    /// <summary>
    /// A percentage of an amount, in exact integer arithmetic, truncated.
    /// </summary>
    /// <remarks>
    /// Truncated to match <see cref="QuantityDiscountCalculator"/>'s existing bands exactly, so a
    /// plan that has been charging a 5% band one way does not start charging it another. Be clear
    /// about which way that leans: truncating a <em>reduction</em> makes the reduction smaller, so it
    /// favours the merchant by up to one minor unit — 5% of 199 takes off 9 rather than the 10 a
    /// rounded rate would. Compatibility with money already being charged is the reason to keep it,
    /// not fairness.
    /// <para>
    /// Widened for the multiplication for the same reason every other calculation here is: an amount
    /// times a basis-point rate overflows a <see cref="long"/> well before the amounts involved look
    /// unreasonable.
    /// </para>
    /// </remarks>
    private static long DiscountOn(long amountMinor, int basisPoints) =>
        (long)((Int128)amountMinor * basisPoints / FullBasisPoints);
}

/// <summary>
/// What came off a charge before any promotional code was considered, and where it came from.
/// </summary>
/// <remarks>
/// Carries both rates and the one that was actually applied, not only the money, because an invoice
/// has to be able to explain a figure months later — by which time the catalogue will have moved.
/// <see cref="SubtotalMinor"/> is always <see cref="GrossAmountMinor"/> less
/// <see cref="DiscountAmountMinor"/>, by construction rather than by a second calculation.
/// </remarks>
/// <param name="GrossAmountMinor">The charge before any reduction.</param>
/// <param name="AutomaticBasisPoints">The price's automatic discount, as authored. Zero when it has none.</param>
/// <param name="QuantityBasisPoints">The band's rate. Zero when the quantity selected no band.</param>
/// <param name="EffectiveBasisPoints">
/// The rate that produced <see cref="DiscountAmountMinor"/> — one of the two, or their sum under
/// <see cref="AutomaticDiscountCombination.Additive"/>. Zero when nothing was taken off.
/// </param>
/// <param name="DiscountAmountMinor">What came off.</param>
/// <param name="SubtotalMinor">What is left to apply a promotion, and then tax, to.</param>
public readonly record struct BuiltInDiscount(
    long GrossAmountMinor,
    int AutomaticBasisPoints,
    int QuantityBasisPoints,
    int EffectiveBasisPoints,
    long DiscountAmountMinor,
    long SubtotalMinor);
