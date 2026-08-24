import { formatMoney, toMinorUnits } from "./subscription-format";

/** Mirrors the server's `TaxMode`. Sent as the name, which is what the API accepts. */
export const TAX_MODES = ["Exclusive", "Inclusive"] as const;

export type TaxMode = (typeof TAX_MODES)[number];

export const TAX_MODE_OPTIONS: { value: TaxMode; label: string; hint: string }[] = [
  {
    value: "Exclusive",
    label: "Added to the price",
    hint: "The amount above is before tax. The customer pays more than it.",
  },
  {
    value: "Inclusive",
    label: "Already in the price",
    hint: "The amount above is what the customer pays. The tax is inside it.",
  },
];

/**
 * A percentage as the API wants it: basis points, so 7.7% is 770 and no fraction is lost on the
 * way. Rounded rather than truncated — 7.7 in binary floating point is 7.699999…, and truncating
 * that would author 7.69% for everyone who typed 7.7.
 */
export const toBasisPoints = (percent: number): number => Math.round(percent * 100);

export const fromBasisPoints = (basisPoints: number): number => basisPoints / 100;

/**
 * The same split the server calculates, for the builder's preview only.
 *
 * The server is authoritative — this never decides what anybody is charged. It exists because an
 * author typing 7.7% against CHF 145.00 cannot otherwise tell the two modes apart, and finding out
 * on a customer's first invoice is the expensive way to learn which one they picked. It follows the
 * server's arithmetic exactly, rounding included, so the preview and the charge agree to the cent.
 */
export const taxBreakdown = ({
  amountMinor,
  basisPoints,
  mode,
}: {
  amountMinor: number;
  basisPoints: number;
  mode: TaxMode;
}): { netMinor: number; taxMinor: number; totalMinor: number } => {
  if (!Number.isFinite(amountMinor) || amountMinor <= 0 || basisPoints <= 0) {
    return { netMinor: Math.max(0, amountMinor), taxMinor: 0, totalMinor: Math.max(0, amountMinor) };
  }

  if (mode === "Inclusive") {
    const taxMinor = Math.round((amountMinor * basisPoints) / (10_000 + basisPoints));

    return { netMinor: amountMinor - taxMinor, taxMinor, totalMinor: amountMinor };
  }

  const taxMinor = Math.round((amountMinor * basisPoints) / 10_000);

  return { netMinor: amountMinor, taxMinor, totalMinor: amountMinor + taxMinor };
};

/**
 * The preview line under the tax fields, in the words a person would use.
 *
 * Returns null when there is nothing to say — no amount yet, or no tax — rather than a sentence
 * about zero.
 */
export const describeTax = ({
  amount,
  currencyCode,
  taxPercent,
  taxMode,
}: {
  amount: number | undefined;
  currencyCode: string;
  taxPercent: number | undefined;
  taxMode: TaxMode;
}): string | null => {
  if (!amount || !taxPercent || !Number.isFinite(amount) || !Number.isFinite(taxPercent)) {
    return null;
  }

  const { netMinor, taxMinor, totalMinor } = taxBreakdown({
    amountMinor: toMinorUnits(amount, currencyCode),
    basisPoints: toBasisPoints(taxPercent),
    mode: taxMode,
  });

  if (taxMinor <= 0) {
    return null;
  }

  return taxMode === "Inclusive"
    ? `${formatMoney(totalMinor, currencyCode)} including ${formatMoney(taxMinor, currencyCode)} tax`
    : `${formatMoney(netMinor, currencyCode)} + ${formatMoney(taxMinor, currencyCode)} tax = ${formatMoney(totalMinor, currencyCode)}`;
};
