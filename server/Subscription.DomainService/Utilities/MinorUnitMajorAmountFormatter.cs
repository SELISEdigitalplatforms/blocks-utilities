using System.Globalization;
using Payment.DomainService.Services;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// Minor units as an invariant major-unit decimal string, with no grouping, no currency code, and
/// no assumption of two decimal places -- e.g. <c>"1.00"</c> (CHF), <c>"100"</c> (JPY),
/// <c>"0.100"</c> (KWD).
/// </summary>
/// <remarks>
/// Unlike <see cref="Services.FinancialDocumentMoneyFormatter"/>, this returns a raw number --
/// no currency code prefix, no thousands separator -- the shape wanted for a JSON field a client
/// reads back as a decimal, not the shape wanted for a line printed on a document. Both derive
/// the currency's decimal places the same way: from the payment module's own resolver, by
/// converting one major unit and counting the minor units it becomes, rather than keeping a
/// second table of currency exponents that could disagree with the one payments actually charges
/// against.
/// <para>
/// Zero is a real price a graduated tier can carry -- a free introductory band -- so, unlike a
/// charged amount, this does not refuse a non-positive minor-unit figure the way the resolver's
/// own <c>TryConvertBack</c> does. A failure here means the currency itself could not be
/// resolved, never that the amount happened to be zero.
/// </para>
/// </remarks>
public static class MinorUnitMajorAmountFormatter
{
    /// <summary>
    /// Converts <paramref name="amountMinor"/> to a major-unit decimal string, or reports it
    /// could not be done rather than fabricating one.
    /// </summary>
    public static bool TryFormat(
        ICurrencyMinorUnitResolver currency,
        long amountMinor,
        string currencyCode,
        out string amount)
    {
        ArgumentNullException.ThrowIfNull(currency);

        amount = string.Empty;

        if (!TryDecimals(currency, currencyCode, out var decimals))
        {
            return false;
        }

        if (amountMinor == 0)
        {
            amount = FormatAmount(0m, decimals);
            return true;
        }

        // TryConvertBack describes a magnitude, not a direction -- re-signed here the same way
        // FinancialDocumentMoneyFormatter handles a credit note's negative figures. Guarded
        // against long.MinValue, whose magnitude does not fit back in a long: a plan-authored
        // tier amount should never be anywhere near that, but this is the one place a corrupted
        // document would otherwise throw instead of simply reporting itself unpriceable.
        long magnitude;

        try
        {
            magnitude = Math.Abs(amountMinor);
        }
        catch (OverflowException)
        {
            return false;
        }

        if (!currency.TryConvertBack(magnitude, currencyCode, out var converted))
        {
            return false;
        }

        var formatted = FormatAmount(converted, decimals);
        amount = amountMinor < 0 ? $"-{formatted}" : formatted;

        return true;
    }

    private static string FormatAmount(decimal amount, int decimals) =>
        amount.ToString(
            "F" + decimals.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);

    /// <summary>
    /// How many decimal places this currency has, asked of the resolver rather than assumed --
    /// see the type remarks for why.
    /// </summary>
    private static bool TryDecimals(ICurrencyMinorUnitResolver currency, string currencyCode, out int decimals)
    {
        decimals = 0;

        if (!currency.TryConvert(1m, currencyCode, out var minorUnitsInOne) || minorUnitsInOne < 1)
        {
            return false;
        }

        while (minorUnitsInOne >= 10 && decimals < 4)
        {
            minorUnitsInOne /= 10;
            decimals++;
        }

        return true;
    }
}
