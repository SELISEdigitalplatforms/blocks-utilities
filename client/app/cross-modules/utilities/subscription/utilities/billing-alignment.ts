import { BILLING_INTERVAL } from "../models/subscription-plan.model";

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
