/**
 * The granularity rules a metered quantity obeys, mirroring the server's `MeterQuantity`.
 *
 * Fractions are opt-in per meter: a meter's `quantityScale` is how many decimal places it accepts,
 * and zero — the default, and what a meter authored before fractions existed reports — means whole
 * units only. Checking it here is so the author is told in the form rather than by a rejected
 * request; the server refuses the same values regardless of what happens in the browser.
 */

/** The finest granularity any meter may declare. Must match `MeterQuantity.MaxScale`. */
export const METER_QUANTITY_MAX_SCALE = 6;

/** The largest magnitude a quantity may take. Must match `MeterQuantity.MaxMagnitude`. */
export const METER_QUANTITY_MAX_MAGNITUDE = 1_000_000_000_000;

/**
 * How many decimal places a number actually carries.
 *
 * Read off the decimal string rather than by arithmetic, because scaling by a power of ten to
 * "check for a remainder" is exactly the kind of binary floating-point step that reports 1.15 as
 * having a remainder. Exponent form is handled because that is how JavaScript renders anything
 * below 1e-6, which is the first thing a six-place meter can hold.
 */
export const scaleOf = (value: number): number => {
  if (!Number.isFinite(value)) {
    return Number.POSITIVE_INFINITY;
  }

  const text = Math.abs(value).toString();
  const exponentAt = text.indexOf("e");

  if (exponentAt >= 0) {
    const mantissa = text.slice(0, exponentAt);
    const power = Number(text.slice(exponentAt + 1));
    const fraction = mantissa.split(".")[1] ?? "";

    return Math.max(0, fraction.length - power);
  }

  const pointAt = text.indexOf(".");

  return pointAt < 0 ? 0 : text.length - pointAt - 1;
};

/** Whether a meter declaring this scale may hold this quantity. */
export const isWithinScale = (value: number, scale: number): boolean =>
  Number.isFinite(value) && scaleOf(value) <= scale;

/** Whether a quantity is inside the representable range. */
export const isWithinMagnitude = (value: number): boolean =>
  Number.isFinite(value) && Math.abs(value) <= METER_QUANTITY_MAX_MAGNITUDE;

/**
 * The `step` a number input should take for a meter at this scale.
 *
 * Without it the browser's own validation rejects a fraction on a `type="number"` field whose step
 * defaults to one, before any of the above is ever consulted.
 */
export const stepFor = (scale: number): string =>
  scale <= 0 ? "1" : (1 / 10 ** scale).toFixed(scale);

/**
 * A quantity as it should be displayed: no trailing zeroes, and no exponent form.
 *
 * A figure that came back from the server's Decimal128 arithmetic can carry places the author
 * never typed, and an allowance of five hundred should not read as `500.000000`.
 */
export const formatQuantity = (value: number): string =>
  Number.isFinite(value)
    ? value.toLocaleString(undefined, {
        minimumFractionDigits: 0,
        maximumFractionDigits: METER_QUANTITY_MAX_SCALE,
      })
    : "";
