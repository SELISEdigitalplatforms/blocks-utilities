import { BILLING_INTERVAL } from "../models/subscription-plan.model";

/**
 * Whether a price's cadence can be aligned to the calendar at all.
 *
 * Monthly, once a month, and nothing else — the same rule the server validates. A fortnight and a
 * quarter both have boundaries that only sometimes land on a first, so there is no honest "the
 * 1st" to offer an author for them.
 *
 * Takes the numeric interval the form holds rather than the name a response carries, because this
 * decides what the plan builder shows while it is being filled in.
 */
export const isCalendarEligible = (price: {
  interval: number;
  intervalCount: number;
}): boolean =>
  price.interval === BILLING_INTERVAL.Month && price.intervalCount === 1;

/**
 * The worked example an author needs to believe the arithmetic.
 *
 * A percentage is not enough on its own: "prorated" describes a dozen different rules, and the one
 * that matters here is that the days are calendar dates counted inclusively. Showing the fraction
 * against a real date is the shortest way to say that.
 */
export const CALENDAR_ALIGNMENT_EXAMPLE =
  "A signup on August 25 pays 7/31 of this monthly price, then renews on September 1.";
