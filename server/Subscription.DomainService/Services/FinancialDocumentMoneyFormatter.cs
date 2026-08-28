using System.Globalization;
using Payment.DomainService.Services;

namespace Subscription.DomainService.Services;

/// <summary>
/// Turns minor units into the string printed on a document.
/// </summary>
/// <remarks>
/// Every amount on a document is stored in minor units, because that is the only representation
/// integer arithmetic can be trusted in. Presentation is the one place it has to become a decimal,
/// and it has to become the right one: a currency's exponent is not always two — yen has none and
/// dinar has three — so both the conversion and the number of decimal places come from the payment
/// module's own resolver rather than from an assumption about hundredths.
/// <para>
/// Invariant culture and an explicit currency code, never a locale-formatted symbol. A document may
/// be read in a country other than the one that issued it, and "1.234,00 €" versus "€1,234.00" is the
/// kind of ambiguity a financial record cannot afford. The code goes beside the number instead.
/// </para>
/// </remarks>
public sealed class FinancialDocumentMoneyFormatter
{
    private readonly ICurrencyMinorUnitResolver _currency;
    private readonly string _currencyCode;
    private readonly int _decimals;
    private readonly bool _known;

    public FinancialDocumentMoneyFormatter(
        ICurrencyMinorUnitResolver currency,
        string currencyCode)
    {
        ArgumentNullException.ThrowIfNull(currency);

        _currency = currency;
        _currencyCode = string.IsNullOrWhiteSpace(currencyCode)
            ? string.Empty
            : currencyCode.ToUpperInvariant();
        _decimals = DecimalsFor(currency, _currencyCode);

        // Asked once, here, because Format cannot tell the two failures apart on its own: the
        // resolver refuses an unconfigured currency and a zero amount with the same false.
        _known = currency.TryConvert(1m, _currencyCode, out var units) && units >= 1;
    }

    public string Format(long amountMinor)
    {
        var negative = amountMinor < 0;

        // Zero is a number, and the resolver will not convert it: TryConvertBack rejects anything
        // at or below zero, so without this a nil subtotal, an untaxed document or a zero total
        // printed as a bare "CHF". On a Total line that reads as missing data rather than as
        // nothing owed, which is the one reading a financial record must never invite.
        if (amountMinor == 0 && _known)
        {
            return $"{_currencyCode} {FormatAmount(0m)}";
        }

        // Converted as a magnitude and re-signed, because the resolver describes an amount rather
        // than a direction — and a credit note's figures are the same amounts pointing the other way.
        if (!_currency.TryConvertBack(Math.Abs(amountMinor), _currencyCode, out var amount))
        {
            // The currency is not configured for payments, which by this point means it was
            // configured when the money moved and has since been removed. Printing the minor units
            // would be wrong by a factor of a hundred, so print the code alone rather than a
            // plausible lie.
            return _currencyCode;
        }

        var text = FormatAmount(amount);

        return negative ? $"-{_currencyCode} {text}" : $"{_currencyCode} {text}";
    }

    private string FormatAmount(decimal amount) =>
        amount.ToString(
            $"N{_decimals.ToString(CultureInfo.InvariantCulture)}",
            CultureInfo.InvariantCulture);

    /// <summary>
    /// How many decimal places this currency has, asked rather than assumed.
    /// </summary>
    /// <remarks>
    /// Derived by converting one major unit and counting the minor units it becomes — 100 for a
    /// two-place currency, 1 for yen, 1000 for dinar. That reuses the resolver's own table instead of
    /// duplicating it here, which is the point: two tables of currency exponents is one table that
    /// will eventually disagree with the amounts actually charged.
    /// </remarks>
    private static int DecimalsFor(ICurrencyMinorUnitResolver currency, string currencyCode)
    {
        if (!currency.TryConvert(1m, currencyCode, out var minorUnitsInOne) || minorUnitsInOne < 1)
        {
            return 2;
        }

        var decimals = 0;
        while (minorUnitsInOne >= 10 && decimals < 4)
        {
            minorUnitsInOne /= 10;
            decimals++;
        }

        return decimals;
    }
}
