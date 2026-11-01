import { formatMoney, toMinorUnits } from "./subscription-format";
import { tSubscription } from "./subscription-i18n";
import { taxBreakdown, toBasisPoints, type TaxMode } from "./subscription-tax";

/** Mirrors the server's `AutomaticDiscountCombination`. Sent as the name, which is what the API accepts. */
export const AUTOMATIC_DISCOUNT_COMBINATIONS = ["BestDiscount", "Additive"] as const;

export type AutomaticDiscountCombination = (typeof AUTOMATIC_DISCOUNT_COMBINATIONS)[number];

export const AUTOMATIC_DISCOUNT_COMBINATION_OPTIONS: {
  value: AutomaticDiscountCombination;
  label: string;
  hint: string;
}[] = [
  {
    value: "BestDiscount",
    get label() {
      return tSubscription("SUBSCRIPTION_DISCOUNT.COMBINATION_BEST");
    },
    get hint() {
      return tSubscription("SUBSCRIPTION_DISCOUNT.COMBINATION_BEST_HINT");
    },
  },
  {
    value: "Additive",
    get label() {
      return tSubscription("SUBSCRIPTION_DISCOUNT.COMBINATION_ADDITIVE");
    },
    get hint() {
      return tSubscription("SUBSCRIPTION_DISCOUNT.COMBINATION_ADDITIVE_HINT");
    },
  },
];

const FULL_BASIS_POINTS = 10_000;

/**
 * What a subscriber pays, and what came off on the way — the same arithmetic the server performs.
 *
 * The server is authoritative; this never decides what anybody is charged. It exists because an
 * author choosing between "use the better discount" and "add the percentages" cannot otherwise see
 * that the two answers differ by real money, and finding out on a customer's first invoice is the
 * expensive way to learn which one they picked.
 *
 * It follows the server exactly, including the directions things round: discounts truncate (the
 * direction that favours the customer, and what the existing volume bands already do) and tax
 * rounds to nearest. A preview that rounded differently would disagree with the charge by a cent,
 * which is worse than showing nothing.
 */
export const discountBreakdown = ({
  grossMinor,
  automaticBasisPoints,
  quantityBasisPoints,
  combination,
}: {
  grossMinor: number;
  automaticBasisPoints: number;
  quantityBasisPoints: number;
  combination: AutomaticDiscountCombination;
}): { discountMinor: number; effectiveBasisPoints: number; subtotalMinor: number } => {
  const automatic = Math.min(Math.max(automaticBasisPoints, 0), FULL_BASIS_POINTS);
  const band = Math.max(quantityBasisPoints, 0);

  if (!Number.isFinite(grossMinor) || grossMinor <= 0 || (automatic === 0 && band === 0)) {
    const subtotalMinor = Math.max(0, grossMinor);

    return { discountMinor: 0, effectiveBasisPoints: 0, subtotalMinor };
  }

  const effectiveBasisPoints =
    automatic === 0
      ? band
      : combination === "Additive"
        ? Math.min(FULL_BASIS_POINTS, automatic + band)
        : Math.max(automatic, band);

  const discountMinor = Math.floor((grossMinor * effectiveBasisPoints) / FULL_BASIS_POINTS);

  return {
    discountMinor,
    effectiveBasisPoints,
    subtotalMinor: grossMinor - discountMinor,
  };
};

/**
 * The whole calculation under a price card, in the words a person would use.
 *
 * One sentence rather than a table, because what an author needs to check is the last number: this
 * is the figure the customer will be charged, and it is the one they can compare against the
 * pricing page they wrote. Returns null when there is nothing to say — no amount yet, or nothing
 * coming off — rather than a sentence about zero.
 */
export const describeAutomaticDiscount = ({
  amount,
  currencyCode,
  quantity,
  automaticDiscountPercent,
  quantityDiscountPercent,
  combination,
  taxPercent,
  taxMode,
}: {
  amount: number | undefined;
  currencyCode: string;
  /** The default quantity the price multiplies. One for a flat fee. */
  quantity: number;
  automaticDiscountPercent: number | undefined;
  quantityDiscountPercent: number;
  combination: AutomaticDiscountCombination;
  taxPercent: number | undefined;
  taxMode: TaxMode;
}): string | null => {
  if (!amount || !Number.isFinite(amount)) {
    return null;
  }

  const units = Number.isFinite(quantity) && quantity > 0 ? quantity : 1;
  const grossMinor = toMinorUnits(amount, currencyCode) * units;

  const { discountMinor, effectiveBasisPoints, subtotalMinor } = discountBreakdown({
    grossMinor,
    automaticBasisPoints: automaticDiscountPercent ? toBasisPoints(automaticDiscountPercent) : 0,
    quantityBasisPoints: quantityDiscountPercent ? toBasisPoints(quantityDiscountPercent) : 0,
    combination,
  });

  if (discountMinor <= 0) {
    return null;
  }

  const { taxMinor, totalMinor } = taxBreakdown({
    amountMinor: subtotalMinor,
    basisPoints: taxPercent ? toBasisPoints(taxPercent) : 0,
    mode: taxMode,
  });

  const off = tSubscription("SUBSCRIPTION_DISCOUNT.OFF", {
    gross: formatMoney(grossMinor, currencyCode),
    discount: formatMoney(discountMinor, currencyCode),
    percent: effectiveBasisPoints / 100,
  });

  // The tax clause is only added when there is tax, and it describes the mode rather than restating
  // it: an inclusive total already contains its tax, so "+ tax" there would be wrong by the tax.
  if (taxMinor <= 0) {
    return tSubscription("SUBSCRIPTION_DISCOUNT.BREAKDOWN_NO_TAX", {
      off,
      subtotal: formatMoney(subtotalMinor, currencyCode),
    });
  }

  return taxMode === "Inclusive"
    ? tSubscription("SUBSCRIPTION_DISCOUNT.BREAKDOWN_INCLUSIVE", {
        off,
        total: formatMoney(totalMinor, currencyCode),
        tax: formatMoney(taxMinor, currencyCode),
      })
    : tSubscription("SUBSCRIPTION_DISCOUNT.BREAKDOWN_EXCLUSIVE", {
        off,
        tax: formatMoney(taxMinor, currencyCode),
        total: formatMoney(totalMinor, currencyCode),
      });
};
