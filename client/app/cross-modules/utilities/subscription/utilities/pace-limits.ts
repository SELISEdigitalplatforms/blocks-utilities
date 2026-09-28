/**
 * The arithmetic behind a meter's pace limits, shared by the schema (which refuses) and the builder
 * (which warns), so the two can never disagree about how long a limit is.
 *
 * Windows are the form's numbers: Hour 0, Day 1, Week 2 — the order of USAGE_WINDOW.
 */
const HOURS_PER_WINDOW = [1, 24, 24 * 7] as const;

/** Allowance periods by BILLING_INTERVAL (Day 0, Week 1, Month 2, Year 3), at their longest. */
const MOST_HOURS_PER_PERIOD = [24, 24 * 7, 24 * 31, 24 * 366] as const;

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
        message: `The shorter limit already caps this stretch at about ${binding.most.toLocaleString()}, so this one never applies.`,
      });
    }
  }

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
