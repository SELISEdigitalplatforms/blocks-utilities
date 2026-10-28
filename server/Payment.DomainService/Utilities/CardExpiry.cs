using System.Globalization;

namespace Payment.DomainService.Utilities;

/// <summary>
/// The month a card stops working, as providers report it: a month and a year, valid through the
/// last day of that month.
/// </summary>
/// <remarks>
/// A value type with a stable <see cref="Key"/> rather than a <see cref="DateTime"/>, because the
/// stored card keeps its expiry as the two strings the provider sent and the key is what is
/// written back to say "this expiry was announced". A card re-saved with a later expiry therefore
/// earns a new notice without anything having to be reset.
/// </remarks>
public readonly record struct CardExpiry(int Year, int Month)
{
    /// <summary><c>yyyy-MM</c>; the value recorded on a card once its expiry notice went out.</summary>
    public string Key => string.Create(CultureInfo.InvariantCulture, $"{Year:0000}-{Month:00}");

    /// <summary>The last calendar day the card is still valid, as a UTC date.</summary>
    public DateTime LastDayUtc =>
        new DateTime(Year, Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1).AddDays(-1);

    /// <summary>The spellings a provider may have stored this expiry under, for a database match.</summary>
    public string[] YearSpellings =>
    [
        Year.ToString("0000", CultureInfo.InvariantCulture),
        (Year % 100).ToString("00", CultureInfo.InvariantCulture)
    ];

    public string[] MonthSpellings =>
    [
        Month.ToString("00", CultureInfo.InvariantCulture),
        Month.ToString(CultureInfo.InvariantCulture)
    ];

    /// <summary>
    /// Every month whose last day falls within the next <paramref name="days"/>, starting with the
    /// current one. Months already past are left out: those cards have expired, not approached it.
    /// </summary>
    public static IReadOnlyList<CardExpiry> EndingWithin(DateTime nowUtc, int days)
    {
        var cutoff = nowUtc.Date.AddDays(Math.Max(0, days));
        var expiries = new List<CardExpiry>();
        var cursor = new CardExpiry(nowUtc.Year, nowUtc.Month);

        while (cursor.LastDayUtc <= cutoff)
        {
            expiries.Add(cursor);
            var next = new DateTime(cursor.Year, cursor.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
            cursor = new CardExpiry(next.Year, next.Month);
        }

        return expiries;
    }

    /// <summary>
    /// Reads what a provider stored. Stripe sends <c>03</c> and <c>2030</c>; Adyen's
    /// <c>expiryDate</c> splits to the same, but a two-digit year is accepted as 20xx in case a
    /// provider abbreviates it.
    /// </summary>
    public static bool TryParse(string? month, string? year, out CardExpiry expiry)
    {
        expiry = default;

        if (!int.TryParse(month, NumberStyles.None, CultureInfo.InvariantCulture, out var m) ||
            !int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var y) ||
            m is < 1 or > 12)
        {
            return false;
        }

        if (y < 100)
        {
            y += 2000;
        }

        if (y is < 2000 or > 2200)
        {
            return false;
        }

        expiry = new CardExpiry(y, m);
        return true;
    }
}
