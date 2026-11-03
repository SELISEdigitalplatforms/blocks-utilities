import { joinList, tSubscription } from "./subscription-i18n";
import type { PlanSummaryData } from "../components/plan-summary-card";

type SummaryMeter = PlanSummaryData["meters"][number];
type SummaryItem = PlanSummaryData["quantityItems"][number];

/**
 * The span a pace is counted over, in words: "an hour", "every 5 hours", "in any 5 hours".
 *
 * Shared by every place that names a pace, because leaving the count or the rolling flag out
 * describes a different, far stricter cap — "1,000 an hour" is five times tighter than the
 * "1,000 every 5 hours" actually sold.
 */
export const describePaceWindow = (window: string, count = 1, rolling = false): string => {
  const span = describeSpan(window, count);

  if (rolling) {
    return tSubscription("MEMBER_PLAN_FORMAT.IN_ANY_SPAN", { span });
  }

  if (count === 1) {
    switch (window) {
      case "Hour":
        return tSubscription("MEMBER_PLAN_FORMAT.PER_HOUR");
      case "Day":
        return tSubscription("MEMBER_PLAN_FORMAT.PER_DAY");
      case "Week":
        return tSubscription("MEMBER_PLAN_FORMAT.PER_WEEK");
      default:
        return window.toLowerCase();
    }
  }

  return tSubscription("MEMBER_PLAN_FORMAT.EVERY_SPAN", { span });
};

const describeSpan = (window: string, count: number): string => {
  const values = { count };

  switch (window) {
    case "Hour":
      return tSubscription("MEMBER_PLAN_FORMAT.SPAN_HOUR", values);
    case "Day":
      return tSubscription("MEMBER_PLAN_FORMAT.SPAN_DAY", values);
    case "Week":
      return tSubscription("MEMBER_PLAN_FORMAT.SPAN_WEEK", values);
    default:
      return count === 1 ? window.toLowerCase() : `${count} ${window.toLowerCase()}`;
  }
};

const describeThen = (behaviour: "Refuse" | "Throttle"): string =>
  behaviour === "Refuse"
    ? tSubscription("MEMBER_PLAN_FORMAT.THEN_REFUSED")
    : tSubscription("MEMBER_PLAN_FORMAT.THEN_OVER_PACE");

/**
 * A meter's pace limits in words, or null when it has none — "at most 1,000 every 5 hours and
 * 20,000 a week, then refused".
 *
 * Says what happens past each as well as where it is, because the two behaviours read
 * identically otherwise and are opposites to whoever is spending: one stops them, the other only
 * tells them. Said once when every limit does the same, against each when they differ.
 */
export const describePace = (meter: SummaryMeter): string | null => {
  const limits = meter.subLimits ?? [];

  if (limits.length === 0) {
    return null;
  }

  const amount = (limit: (typeof limits)[number]) =>
    tSubscription("MEMBER_PLAN_FORMAT.PACE_AMOUNT", {
      quantity: limit.quantity.toLocaleString(),
      window: describePaceWindow(limit.window, limit.windowCount, limit.rolling),
    });

  const behaviours = new Set(limits.map((limit) => limit.behaviour));

  if (behaviours.size === 1) {
    return tSubscription("MEMBER_PLAN_FORMAT.AT_MOST_THEN", {
      limits: joinList(limits.map(amount)),
      then: describeThen(limits[0].behaviour),
    });
  }

  return tSubscription("MEMBER_PLAN_FORMAT.AT_MOST", {
    limits: joinList(
      limits.map((limit) =>
        tSubscription("MEMBER_PLAN_FORMAT.LIMIT_WITH_BEHAVIOUR", {
          amount: amount(limit),
          then: describeThen(limit.behaviour),
        }),
      ),
    ),
  });
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
    const each = tSubscription("MEMBER_PLAN_FORMAT.ALLOWANCE_EACH", {
      count: meter.includedQuantity,
      quantity: meter.includedQuantity.toLocaleString(),
      unit: meter.unitLabel,
    });
    const pace = describePace(meter);

    return pace ? tSubscription("MEMBER_PLAN_FORMAT.ALLOWANCE_WITH_PACE", { each, pace }) : each;
  });

  // Semicolons between the parts, because each part already uses commas: with two meters, one
  // comma-joined run read "…then refused, 100 calls each, at most…" with no telling where one
  // meter stopped and the next began.
  const sentence = (places: string) =>
    `${[places, ...allowances].join("; ")}.`;

  const counting = countingItemOf(plan);

  if (!counting) {
    return plan.quantityItems.length === 0
      ? sentence(tSubscription("MEMBER_PLAN_FORMAT.ONE_PLACE"))
      : tSubscription("MEMBER_PLAN_FORMAT.MARK_COUNTING_ITEM");
  }

  // The same item means "how many were bought" under a per-unit price and "how many may be
  // bought" under a flat one, so each price shape answers separately. A plan can carry both.
  const perPlace = plan.prices.some((price) => price.quantityItemKey === counting.itemKey);
  const flat = plan.prices.some((price) => price.quantityItemKey !== counting.itemKey);
  const perPlaceText =
    perPlace || plan.prices.length === 0
      ? tSubscription("MEMBER_PLAN_FORMAT.PLACES_PER_ITEM", {
          unit: counting.unitLabel,
          default: counting.defaultQuantity.toLocaleString(),
        })
      : null;
  const flatText = flat ? describeFlatPlaces(counting, perPlaceText !== null) : null;

  if (perPlaceText && flatText) {
    return sentence(
      tSubscription("MEMBER_PLAN_FORMAT.PLACES_BOTH", { perPlace: perPlaceText, flat: flatText }),
    );
  }

  return sentence(perPlaceText ?? flatText ?? "");
};

const describeFlatPlaces = (counting: SummaryItem, afterPerPlace: boolean): string => {
  if (counting.maxQuantity === null) {
    return tSubscription(
      afterPerPlace
        ? "MEMBER_PLAN_FORMAT.PLACES_FLAT_NO_MAX_ALSO"
        : "MEMBER_PLAN_FORMAT.PLACES_FLAT_NO_MAX",
    );
  }

  return tSubscription("MEMBER_PLAN_FORMAT.PLACES_FLAT", {
    count: counting.maxQuantity,
    max: counting.maxQuantity.toLocaleString(),
  });
};
