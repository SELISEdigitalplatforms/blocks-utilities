import type {
  SubscriptionPreviewNextCharge,
  SubscriptionPreviewRenewal,
} from "../models/subscription-simulation.model";

/**
 * Whether the charge actually due next is a different figure from the steady-state recurring
 * price — and so has to be shown in its own right rather than left off the quote.
 *
 * Two separate ways they part company, which is why the amounts are compared and not only the
 * `prorated` flag:
 *
 * - a calendar-aligned trial converting mid-month buys a shorter, prorated stub (`prorated`);
 * - a promotional code with a limited duration can be spent by the time the next charge falls
 *   due. `nextRenewal` prices a full period against the discount periods applied *today*, while
 *   `nextCharge` projects the consumption the opening payment will have made — so a one-period
 *   code makes the two disagree on the same full period, on the same date, with nothing prorated.
 *
 * The second case is the one that matters for display: left to `prorated` alone, a subscriber
 * reads a renewal figure lower than what will actually be taken.
 */
export const nextChargeDiffersFromRenewal = (quote: {
  nextCharge: SubscriptionPreviewNextCharge;
  nextRenewal: SubscriptionPreviewRenewal;
}): boolean =>
  quote.nextCharge.prorated ||
  quote.nextCharge.totalMinor !== quote.nextRenewal.totalMinor;
