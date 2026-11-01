import { tSubscription } from "./subscription-i18n";

/**
 * An entitlement and its meter answer two different questions — "may they?" and "what does it
 * cost?" — and nothing on the server makes them agree. A plan can permit 500 signatures while
 * the meter includes 150, and both halves behave exactly as authored: the app is told the
 * customer is allowed all 500, and 350 of them are billed as overage. That is a real
 * configuration, so this reports it rather than rejecting it.
 */
export interface EntitlementConsistencyInput {
  /** The stringified limit kind a response carries — "Boolean", "Count" or "Unlimited". */
  limitKind: string;
  limit: number | null;
  meterKey: string | null;
}

export interface MeterConsistencyInput {
  meterKey: string;
  unitLabel: string;
  includedQuantity: number;
  overageAllowed: boolean;
}

const plural = (count: number, unitLabel: string) =>
  tSubscription("PLAN_CONSISTENCY.UNIT_COUNT", {
    count,
    quantity: count.toLocaleString(),
    unit: unitLabel,
  });

/**
 * What is inconsistent between a counted entitlement and the meter that draws it down, in the
 * words of what will actually happen to a subscriber. Null when the two agree, or when there is
 * nothing to compare — an uncounted entitlement, or a meter this plan does not define yet.
 */
export const describeEntitlementMeterMismatch = (
  entitlement: EntitlementConsistencyInput,
  meters: MeterConsistencyInput[],
): string | null => {
  if (entitlement.limitKind !== "Count" || entitlement.limit === null || !entitlement.meterKey) {
    return null;
  }

  const meter = meters.find((candidate) => candidate.meterKey === entitlement.meterKey);

  if (!meter) {
    return null;
  }

  const limit = entitlement.limit;
  const included = meter.includedQuantity;

  if (limit === included) {
    return null;
  }

  const unit = meter.unitLabel || tSubscription("PLAN_CONSISTENCY.UNIT_FALLBACK");

  if (limit > included) {
    const excess = limit - included;

    return meter.overageAllowed
      ? tSubscription("PLAN_CONSISTENCY.PERMITS_OVER_BILLED", {
          count: excess,
          limit: plural(limit, unit),
          included: included.toLocaleString(),
          excess: plural(excess, unit),
        })
      : tSubscription("PLAN_CONSISTENCY.PERMITS_OVER_BLOCKED", {
          limit: plural(limit, unit),
          included: included.toLocaleString(),
          excess: plural(excess, unit),
        });
  }

  return tSubscription("PLAN_CONSISTENCY.PERMITS_UNDER", {
    limit: plural(limit, unit),
    included: included.toLocaleString(),
    unused: plural(included - limit, unit),
  });
};
