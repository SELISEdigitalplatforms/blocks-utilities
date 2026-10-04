/**
 * The arithmetic behind a meter's pace limits, shared by the schema (which refuses) and the builder
 * (which warns), so the two can never disagree about how long a limit is.
 *
 * Windows are the form's numbers: Hour 0, Day 1, Week 2 — the order of USAGE_WINDOW.
 */
const HOURS_PER_WINDOW = [1, 24, 24 * 7] as const;

/** Allowance periods by BILLING_INTERVAL (Day 0, Week 1, Month 2, Year 3), at their longest. */
const MOST_HOURS_PER_PERIOD = [24, 24 * 7, 24 * 31, 24 * 366] as const;

/**
 * The same periods at their shortest. Used where more periods is the generous reading: a span
 * touches the most allowance periods when they are as short as they get — a week can reach
 * across two months, and that holds for February too.
 */
const LEAST_HOURS_PER_PERIOD = [24, 24 * 7, 24 * 28, 24 * 365] as const;

const UNIT_NAMES = ["hour", "day", "week"] as const;
const ONE_OF = ["an hour", "a day", "a week"] as const;

/** "a week", "5 hours" — the stretch a limit covers, for naming it in a sentence. */
const stretch = (limit: Pick<PaceLimit, "window" | "count">): string =>
  limit.count <= 1
    ? (ONE_OF[limit.window] ?? "a window")
    : `${limit.count} ${UNIT_NAMES[limit.window] ?? "window"}s`;

/** "10,000 per 5 hours" — a limit as its author wrote it. */
const named = (limit: PaceLimit): string =>
  `${(limit.quantity ?? 0).toLocaleString()} per ${limit.count <= 1 ? "" : `${limit.count} `}${UNIT_NAMES[limit.window] ?? "window"}${limit.count <= 1 ? "" : "s"}`;

export const MAX_PACE_LIMITS = 3;

export interface PaceLimit {
  window: number;
  count: number;
  rolling: boolean;
  quantity?: number;
  /** Refuse 0, Report 1. */
  behaviour: number;
}

/** A limit's length in hours, so a day and twenty-four hours compare as the same limit. */
export const spanHours = (limit: Pick<PaceLimit, "window" | "count">): number =>
  Math.max(1, limit.count) * (HOURS_PER_WINDOW[limit.window] ?? 1);

/**
 * The most a refusing limit lets through over a longer stretch. A fixed limit can be caught
 * across one extra block — a stretch rarely starts where a block does — so it gets one more.
 */
const mostOver = (limit: PaceLimit, hours: number): number =>
  (limit.quantity ?? 0) * (Math.ceil(hours / spanHours(limit)) + (limit.rolling ? 0 : 1));

export interface PaceWarning {
  /** Which limit the warning is about, so it can be shown against that row. */
  index: number;
  message: string;
}

/**
 * The combinations that save but probably do not mean what they say.
 *
 * Warnings rather than refusals: the bounds here are upper estimates once fixed and rolling
 * limits meet, and refusing on an estimate would block plans that are in fact valid. The certain
 * mistakes are refused in the schema instead.
 *
 * Only refusing limits make another unreachable — one that only reports stops nothing.
 */
export const paceWarnings = (
  limits: PaceLimit[],
  meter: {
    includedQuantity: number;
    usageInterval: number;
    usageIntervalCount: number;
    /** 1 is a lifetime allowance, which has no period to compare against. */
    resetPolicy: number;
    /** With overage on, usage runs past the included amount, and a limit is what caps it. */
    overageAllowed: boolean;
    /** What a carry-forward meter may add to one period on top of its included amount. */
    carryForwardCap?: number;
  },
): PaceWarning[] => {
  const warnings: PaceWarning[] = [];
  const sized = limits
    .map((limit, index) => ({ limit, index }))
    .filter(({ limit }) => limit.quantity !== undefined && limit.quantity > 0);
  const refusing = sized.filter(({ limit }) => limit.behaviour === 0);

  // A longer limit the shorter ones already hold it under.
  for (const longer of sized) {
    const binding = refusing
      .filter(({ limit }) => spanHours(limit) < spanHours(longer.limit))
      .map(({ limit }) => ({ limit, most: mostOver(limit, spanHours(longer.limit)) }))
      .sort((a, b) => a.most - b.most)[0];

    if (binding && (longer.limit.quantity ?? 0) >= binding.most) {
      warnings.push({
        index: longer.index,
        message: `The limit of ${named(binding.limit)} already caps ${stretch(longer.limit)} at about ${binding.most.toLocaleString()}, so this one never applies.`,
      });
    }
  }

  warnings.push(...budgetWarnings(sized, meter));

  if (meter.resetPolicy === 1) {
    return warnings;
  }

  const periodHours =
    (MOST_HOURS_PER_PERIOD[meter.usageInterval] ?? MOST_HOURS_PER_PERIOD[2]) *
    Math.max(1, meter.usageIntervalCount);

  // The included amount out of reach: the limits let less through in a period than it holds.
  const tightest = refusing
    .map(({ limit }) => mostOver(limit, periodHours))
    .sort((a, b) => a - b)[0];

  if (tightest !== undefined && tightest < meter.includedQuantity) {
    warnings.push({
      index: -1,
      message: `These limits let through at most about ${tightest.toLocaleString()} per allowance period, so the ${meter.includedQuantity.toLocaleString()} included can never all be used.`,
    });
  }

  // A fixed limit exactly as long as the allowance period only restates it on a different clock.
  sized.forEach(({ limit, index }) => {
    const isDayOrWeekPeriod = meter.usageInterval <= 1 && meter.usageIntervalCount === 1;

    if (!limit.rolling && isDayOrWeekPeriod && spanHours(limit) === periodHours) {
      warnings.push({
        index,
        message:
          "This covers the same stretch as the allowance period, so the smaller of the two is the real cap. Make it rolling, or shorter, if you meant to limit how fast it is spent.",
      });
    }
  });

  return warnings;
};

/**
 * A limit the included amount already holds usage under.
 *
 * With overage off, a meter stops at its budget: the included amount per allowance period (plus
 * whatever a carry-forward meter may bring in). A limit's span can touch at most one period more
 * than it fully covers — a rolling week straddling a month end sees the last of one month and the
 * first of the next — so that many budgets is the most it could ever be asked to hold. A limit at
 * or above that never refuses anything the budget has not refused first.
 *
 * With overage on nothing stops at the budget, and a limit is what caps how fast overage is run
 * up — the job it is most often there for — so there is nothing to warn about.
 */
const budgetWarnings = (
  sized: { limit: PaceLimit; index: number }[],
  meter: Parameters<typeof paceWarnings>[1],
): PaceWarning[] => {
  if (meter.overageAllowed || meter.includedQuantity <= 0) {
    return [];
  }

  // A lifetime allowance is one budget for ever; no span can see more of it than the whole.
  if (meter.resetPolicy === 1) {
    return sized
      .filter(({ limit }) => (limit.quantity ?? 0) >= meter.includedQuantity)
      .map(({ index }) => ({
        index,
        message: `The meter stops at ${meter.includedQuantity.toLocaleString()} for its whole lifetime, so this limit can never be reached.`,
      }));
  }

  const budget =
    meter.includedQuantity + (meter.resetPolicy === 2 ? (meter.carryForwardCap ?? 0) : 0);
  const shortestPeriod =
    (LEAST_HOURS_PER_PERIOD[meter.usageInterval] ?? LEAST_HOURS_PER_PERIOD[2]) *
    Math.max(1, meter.usageIntervalCount);

  return sized
    .map(({ limit, index }) => ({
      index,
      limit,
      most: budget * (Math.ceil(spanHours(limit) / shortestPeriod) + 1),
    }))
    .filter(({ limit, most }) => (limit.quantity ?? 0) >= most)
    .map(({ index, most }) => ({
      index,
      message: `With overage off the meter stops at ${budget.toLocaleString()} per allowance period, so no stretch this long can hold more than ${most.toLocaleString()} and this limit can never be reached.`,
    }));
};
