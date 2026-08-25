using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// Says which of the two built-in discounts a recorded reduction came from.
/// </summary>
/// <remarks>
/// The money path settles the price's automatic discount against the volume band and records one
/// figure, because one figure is what comes off the charge — see <c>BuiltInDiscountCalculator</c>. A
/// document has to show the two separately: "8% annual discount" and "5% volume discount" are
/// different promises to the subscriber, and a single "discount" line cannot say which was kept.
/// <para>
/// This is a re-attribution, not a second calculation. It never changes the total that came off; it
/// only says how the total divides. That is exactly recoverable from the two rates and the price's
/// combination, which the payment record already carries — under
/// <see cref="AutomaticDiscountCombination.BestDiscount"/> one rate won outright and took all of it,
/// and under <see cref="AutomaticDiscountCombination.Additive"/> the two were summed into one rate and
/// divide in proportion to their parts.
/// </para>
/// <para>
/// Reading it back rather than storing a third and fourth number is deliberate. Two stored figures
/// that must always sum to a third stored figure is two chances for a document to contradict itself,
/// and the arithmetic that reconciles them is this function either way.
/// </para>
/// </remarks>
public static class BuiltInDiscountAttribution
{
    /// <param name="builtInDiscountMinor">The single figure the money path recorded.</param>
    /// <param name="automaticBasisPoints">The price's automatic rate as charged. Null or zero if none.</param>
    /// <param name="quantityBasisPoints">The volume band's rate as charged. Null or zero if none.</param>
    /// <param name="combination">
    /// The stored wire value of the price's combination — what
    /// <c>SubscriptionDiscountPresentation.Describe</c> wrote. Anything other than
    /// <see cref="AutomaticDiscountCombination.Additive"/>, including null, is read as best-discount:
    /// the conservative reading, and the one every price authored before combinations existed was
    /// charged under.
    /// </param>
    public static (long AutomaticMinor, long QuantityMinor) Split(
        long builtInDiscountMinor,
        int? automaticBasisPoints,
        int? quantityBasisPoints,
        string? combination)
    {
        if (builtInDiscountMinor <= 0)
        {
            return (0, 0);
        }

        var automatic = Math.Max(0, automaticBasisPoints ?? 0);
        var quantity = Math.Max(0, quantityBasisPoints ?? 0);

        if (IsAdditive(combination) && automatic > 0 && quantity > 0)
        {
            // Split by the parts that were summed into the applied rate. Largest remainder rather
            // than two independent roundings, so the two lines always add back to the figure the
            // subscriber was charged.
            var parts = ProportionalAllocation.Split(
                builtInDiscountMinor,
                [automatic, quantity]);

            return (parts[0], parts[1]);
        }

        // Best discount: one of them won and the other took nothing. The band wins only by being
        // strictly larger — a tie goes to the price's own rate, which is what the calculator does and
        // for the same reason: the money is identical either way, and reporting the authored rate keeps
        // the document consistent with the price it names.
        return quantity > automatic
            ? (0, builtInDiscountMinor)
            : automatic > 0
                ? (builtInDiscountMinor, 0)
                : (0, builtInDiscountMinor);
    }

    private static bool IsAdditive(string? combination) =>
        string.Equals(
            combination,
            nameof(AutomaticDiscountCombination.Additive),
            StringComparison.OrdinalIgnoreCase);
}
