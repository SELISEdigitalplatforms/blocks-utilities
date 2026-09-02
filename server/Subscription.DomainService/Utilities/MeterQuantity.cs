using System.Globalization;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// The rules a metered quantity obeys, and the one place a fractional charge becomes money.
/// </summary>
/// <remarks>
/// Quantities are <see cref="decimal"/> — exact base-ten, stored as BSON <c>Decimal128</c> — rather
/// than <see cref="double"/>. A reversal has to cancel the entry it compensates to the last place,
/// and binary floating point cannot promise that: <c>0.1 + 0.2 - 0.3</c> leaves a residue that would
/// sit in a customer's balance forever.
/// <para>
/// Fractions are opt-in per meter. A meter's <c>QuantityScale</c> is how many decimal places it
/// accepts, and it defaults to zero — so a meter that counts screenings keeps refusing half of one,
/// exactly as it did when these fields were integers. A plan already in the database has no such
/// field, deserializes to zero, and therefore cannot behave differently.
/// </para>
/// </remarks>
public static class MeterQuantity
{
    /// <summary>
    /// The finest granularity any meter may declare.
    /// </summary>
    /// <remarks>
    /// Six places covers the units that prompted this — storage in GB, time in hours, prorated
    /// credits — and keeps every quantity below roughly nine billion exactly representable as an
    /// IEEE-754 double, so a browser renders the figure the server computed rather than one that
    /// drifted in transit.
    /// </remarks>
    public const int MaxScale = 6;

    /// <summary>The largest magnitude a quantity may take.</summary>
    /// <remarks>
    /// Decimal128 holds more than <see cref="decimal"/> does, so a value written by something other
    /// than this service could otherwise overflow on the way back in. Bounding it at authoring and
    /// recording time means that never happens. Generous enough that no real meter reaches it, and
    /// it still leaves headroom for the tier multiplication below.
    /// </remarks>
    public const decimal MaxMagnitude = 1_000_000_000_000m;

    /// <summary>How many decimal places a value actually carries, trailing zeroes ignored.</summary>
    /// <remarks>
    /// Read from the decimal's own scale after normalising, because <c>1.50m</c> and <c>1.5m</c> are
    /// equal but differently scaled — refusing the first on a one-place meter would be arbitrary.
    /// </remarks>
    public static int ScaleOf(decimal value)
    {
        var scale = (decimal.GetBits(value)[3] >> 16) & 0xFF;

        // Rounding to one place fewer leaves the value untouched exactly when the last place holds
        // a zero, so this walks off the trailing zeroes without any string formatting.
        while (scale > 0 && value == decimal.Round(value, scale - 1))
        {
            scale--;
        }

        return scale;
    }

    /// <summary>Whether a meter declaring <paramref name="scale"/> may hold this quantity.</summary>
    public static bool IsWithinScale(decimal value, int scale) =>
        scale >= 0 &&
        scale <= MaxScale &&
        ScaleOf(value) <= scale;

    /// <summary>Whether a quantity is inside the representable range.</summary>
    public static bool IsWithinMagnitude(decimal value) =>
        Math.Abs(value) <= MaxMagnitude;

    /// <summary>Whether a scale is one a meter may declare at all.</summary>
    public static bool IsValidScale(int scale) =>
        scale is >= 0 and <= MaxScale;

    /// <summary>
    /// Turns an exactly-computed charge into whole minor units.
    /// </summary>
    /// <remarks>
    /// The single rounding event in usage rating. Tier arithmetic stays exact all the way through —
    /// so re-banding a rate table without changing any of its prices cannot change the bill — and
    /// only the meter's own total is rounded, because that total is the invoice line the customer
    /// actually sees.
    /// <para>
    /// Away from zero at the midpoint: half a minor unit rounds up, which is the convention an
    /// invoice reader expects and which a reversal mirrors symmetrically, since the sign is carried
    /// through rather than truncated toward it.
    /// </para>
    /// </remarks>
    public static long ToMinorUnits(decimal exactAmountMinor)
    {
        var rounded = decimal.Round(exactAmountMinor, 0, MidpointRounding.AwayFromZero);

        // Checked, for the same reason the tier walk is: a technically valid rate and a technically
        // valid quantity can each pass validation alone and still multiply into something no
        // long-minor amount can hold. Refusing beats silently wrapping the charge.
        return checked((long)rounded);
    }

    /// <summary>
    /// The smallest quantity a meter at this scale can distinguish: <c>1</c> at scale zero,
    /// <c>0.001</c> at scale three.
    /// </summary>
    /// <remarks>
    /// Used to name the first quantity inside a rate band whose lower bound is exclusive. At scale
    /// zero that lands on the whole unit the band has always started at, so a plan authored before
    /// fractions existed reports exactly the bands it reported before.
    /// </remarks>
    public static decimal SmallestStep(int scale) =>
        scale switch
        {
            <= 0 => 1m,
            1 => 0.1m,
            2 => 0.01m,
            3 => 0.001m,
            4 => 0.0001m,
            5 => 0.00001m,
            _ => 0.000001m
        };

    /// <summary>A quantity as it should appear in a log line or an error message.</summary>
    public static string Describe(decimal value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}
