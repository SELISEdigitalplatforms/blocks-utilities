namespace Subscription.DomainService.Utilities;

/// <summary>
/// Splits an amount of money across weighted parts so the parts sum to exactly the amount.
/// </summary>
/// <remarks>
/// A partial refund has to reverse a proportion of each thing the original invoice charged for — its
/// discounts, its tax, each of its lines. Multiplying every part by a fraction and rounding each one
/// independently does not add up: three thirds of 100 rounded down is 99, and the missing minor unit
/// is a credit note that does not reconcile against the invoice it adjusts.
/// <para>
/// Largest remainder fixes that by construction. Each part gets the floor of its exact share, and the
/// units left over by flooring are handed out one each to the parts with the largest discarded
/// fractions. The result is the closest integer split to the exact one whose total is right, and it is
/// deterministic: the same inputs give the same answer on every machine and every replay, which
/// matters because a credit note may be issued by whichever worker picks the work up.
/// </para>
/// <para>
/// Ties are broken by position, so two parts with identical weights always resolve the same way rather
/// than by whatever order a sort happened to leave them in.
/// </para>
/// </remarks>
public static class ProportionalAllocation
{
    /// <param name="amountMinor">
    /// The total to hand out. Negative totals are allocated as their magnitude and re-signed, so a
    /// reversal splits exactly the way the charge it reverses did.
    /// </param>
    /// <param name="weights">
    /// What each part is entitled to in proportion to. Negative weights are read as zero — a part
    /// cannot be owed a negative share of a reversal.
    /// </param>
    /// <returns>
    /// One amount per weight, in the same order, summing to <paramref name="amountMinor"/> exactly.
    /// All zeroes when every weight is zero: with nothing to apportion by, spreading the total evenly
    /// would be an invention, and giving it all to the first part would be an arbitrary one.
    /// </returns>
    public static long[] Split(long amountMinor, IReadOnlyList<long> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        var result = new long[weights.Count];
        if (weights.Count == 0 || amountMinor == 0)
        {
            return result;
        }

        var sign = amountMinor < 0 ? -1 : 1;
        var magnitude = (Int128)Math.Abs(amountMinor);

        Int128 total = 0;
        for (var index = 0; index < weights.Count; index++)
        {
            total += Math.Max(0, weights[index]);
        }

        if (total == 0)
        {
            return result;
        }

        // The floor of each exact share, and the remainder it discarded. Kept as the numerator of the
        // fraction rather than the fraction itself, so the comparison below is exact integer work.
        var remainders = new (Int128 Remainder, int Index)[weights.Count];
        Int128 allocated = 0;

        for (var index = 0; index < weights.Count; index++)
        {
            var weight = (Int128)Math.Max(0, weights[index]);
            var exact = magnitude * weight;
            var share = exact / total;

            result[index] = (long)share * sign;
            allocated += share;
            remainders[index] = (exact - (share * total), index);
        }

        var leftover = (int)(magnitude - allocated);
        if (leftover <= 0)
        {
            return result;
        }

        Array.Sort(
            remainders,
            (left, right) => right.Remainder != left.Remainder
                ? (right.Remainder > left.Remainder ? 1 : -1)
                : left.Index.CompareTo(right.Index));

        for (var position = 0; position < leftover; position++)
        {
            result[remainders[position].Index] += sign;
        }

        return result;
    }
}
