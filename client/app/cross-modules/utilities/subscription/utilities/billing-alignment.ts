import { BILLING_INTERVAL } from "../models/subscription-plan.model";
import { FLAT_FEE } from "../schemas/subscription-price.schema";

/** A year, in months. Named because a bare 12 in a money calculation is not obvious. */
export const MONTHS_IN_A_YEAR = 12;

/**
 * Whether a price's cadence can be aligned to the calendar at all.
 *
 * Once a month or once a year, and nothing else — the same rule the server validates. A fortnight
 * and a quarter both have boundaries that only sometimes land on a first, so there is no honest
 * "the 1st" to offer an author for them.
 *
 * Takes the numeric interval the form holds rather than the name a response carries, because this
 * decides what the plan builder shows while it is being filled in.
 */
export const isCalendarEligible = (price: {
  interval: number;
  intervalCount: number;
}): boolean =>
  price.intervalCount === 1 &&
  (price.interval === BILLING_INTERVAL.Month || price.interval === BILLING_INTERVAL.Year);

/**
 * Whether this price prices its opening stub from a separate monthly price.
 *
 * Yearly only. A month's stub is a fraction of the very price being charged; a year's cannot be,
 * because days of an annual amount are not a quantity anybody can charge. The monthly equivalent
 * has to be named.
 */
export const needsStubBasePrice = (price: {
  interval: number;
  intervalCount: number;
}): boolean =>
  price.interval === BILLING_INTERVAL.Year && price.intervalCount === 1;

/** Whether this price is authored as calendar-aligned *and* on a cadence that allows it. */
export const isCalendarAligned = (price: {
  interval: number;
  intervalCount: number;
  billingAlignment?: string;
}): boolean =>
  price.billingAlignment === "CalendarMonth" && isCalendarEligible(price);

/** Whether the author has to pick a monthly counterpart for this price. */
export const requiresStubBasePrice = (price: {
  interval: number;
  intervalCount: number;
  billingAlignment?: string;
}): boolean => isCalendarAligned(price) && needsStubBasePrice(price);

/**
 * Whether a monthly price can be the stub basis for a yearly one being authored.
 *
 * Mirrors the server's own check, which refuses anything else. The stub is charged from this
 * price's amount and the annual period from the yearly one, so a mismatch in what they multiply or
 * how they are taxed produces two figures a subscriber cannot reconcile — and offering the choice
 * in the picker only to have the API reject it is the worst version of that.
 *
 * Currency, quantity item and tax must all agree. The cadence is already guaranteed by the caller
 * filtering to monthly prices.
 */
export const isCompatibleStubBasis = (
  candidate: {
    currencyCode: string;
    quantityItemKey?: string | null;
    taxRateBasisPoints?: number | null;
    taxMode?: string | null;
  },
  yearly: {
    currencyCode: string;
    quantityItemKey?: string | null;
    taxPercent?: number;
    taxMode?: string;
  },
): boolean => {
  if (candidate.currencyCode !== yearly.currencyCode) {
    return false;
  }

  // The sentinel the price form uses for "flat fee" never leaves that form, so compare on the
  // absence of a key rather than on its spelling.
  const candidateItem = candidate.quantityItemKey ?? "";
  const yearlyItem =
    !yearly.quantityItemKey || yearly.quantityItemKey === FLAT_FEE ? "" : yearly.quantityItemKey;

  if (candidateItem !== yearlyItem) {
    return false;
  }

  const candidateRate = candidate.taxRateBasisPoints ?? 0;
  const yearlyRate = yearly.taxPercent ? Math.round(yearly.taxPercent * 100) : 0;

  if (candidateRate !== yearlyRate) {
    return false;
  }

  // Mode only matters where there is a rate for it to describe, and an absent mode reads as
  // exclusive — which is how the server charges a price authored before modes existed.
  return (
    candidateRate === 0 ||
    (candidate.taxMode ?? "Exclusive") === (yearly.taxMode ?? "Exclusive")
  );
};

/**
 * The worked example an author needs to believe the arithmetic.
 *
 * A percentage is not enough on its own: "prorated" describes a dozen different rules, and the one
 * that matters here is that the days are calendar dates counted inclusively. Showing the fraction
 * against a real date is the shortest way to say that.
 */
export const CALENDAR_ALIGNMENT_EXAMPLE =
  "A signup on August 25 pays 7/31 of this monthly price, then renews on September 1.";

export const CALENDAR_YEARLY_ALIGNMENT_EXAMPLE =
  "A signup on August 25 pays 7/31 of the monthly price above, then a full year from September 1.";
