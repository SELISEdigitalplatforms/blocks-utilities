import type { PlanSummaryData } from "../components/plan-summary-card";

type SummaryMeter = PlanSummaryData["meters"][number];
type SummaryItem = PlanSummaryData["quantityItems"][number];

const PER_WINDOW: Record<string, string> = { Hour: "an hour", Day: "a day", Week: "a week" };

/**
 * A meter's pace cap in words, or null when it has none — "at most 1,000 an hour, then refused".
 *
 * Says what happens past it as well as where it is, because the two behaviours read identically
 * otherwise and are opposites to whoever is spending: one stops them, the other only tells them.
 */
export const describePace = (meter: SummaryMeter): string | null => {
  if (!meter.subLimitWindow || meter.subLimitQuantity == null) {
    return null;
  }

  const then = meter.subLimitBehaviour === "Throttle" ? "then reported as over pace" : "then refused";

  return `at most ${meter.subLimitQuantity.toLocaleString()} ${PER_WINDOW[meter.subLimitWindow]}, ${then}`;
};

/**
 * The quantity that counts people, mirroring the server: the only item when there is one, else
 * the one marked. Undefined when there are none (one person's plan) or the mark is ambiguous.
 */
export const countingItemOf = (plan: PlanSummaryData): SummaryItem | undefined => {
  if (plan.quantityItems.length === 1) {
    return plan.quantityItems[0];
  }

  const marked = plan.quantityItems.filter((item) => item.countsMembers);

  return marked.length === 1 ? marked[0] : undefined;
};

/**
 * A user-wise plan's whole shape in one sentence — "10 places, 10,000,000 tokens each, at most
 * 1,000,000 an hour." Shown on the review step, where an author catches a wrong toggle.
 */
export const describePlaces = (plan: PlanSummaryData): string => {
  const allowances = plan.meters.map((meter) => {
    const each = `${meter.includedQuantity.toLocaleString()} ${plural(meter.unitLabel, meter.includedQuantity)} each`;
    const pace = describePace(meter);

    return pace ? `${each}, ${pace}` : each;
  });

  const sentence = (places: string) =>
    `${[places, ...allowances].join(", ")}.`;

  const counting = countingItemOf(plan);

  if (!counting) {
    return plan.quantityItems.length === 0
      ? sentence("One place")
      : "Mark which quantity counts people — until then nobody can be given a place.";
  }

  // The same item means "how many were bought" under a per-unit price and "how many may be
  // bought" under a flat one, so each price shape answers separately. A plan can carry both.
  const perPlace = plan.prices.some((price) => price.quantityItemKey === counting.itemKey);
  const flat = plan.prices.some((price) => price.quantityItemKey !== counting.itemKey);
  const counts: string[] = [];

  if (perPlace || plan.prices.length === 0) {
    counts.push(`one place per ${counting.unitLabel} bought (${counting.defaultQuantity.toLocaleString()} by default)`);
  }

  if (flat) {
    counts.push(
      counting.maxQuantity === null
        ? "no maximum set, so a flat price has no places to give"
        : `${counting.maxQuantity.toLocaleString()} ${plural("place", counting.maxQuantity)} at the flat price`,
    );
  }

  const places = counts.join(", or ");

  return sentence(places.charAt(0).toUpperCase() + places.slice(1));
};

const plural = (label: string, quantity: number) => (quantity === 1 ? label : `${label}s`);
