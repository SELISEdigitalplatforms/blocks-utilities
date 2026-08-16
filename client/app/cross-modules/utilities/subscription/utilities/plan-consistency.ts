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
  `${count.toLocaleString()} ${unitLabel}${count === 1 ? "" : "s"}`;

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

  const unit = meter.unitLabel || "unit";

  if (limit > included) {
    const excess = limit - included;

    return meter.overageAllowed
      ? `Permits ${plural(limit, unit)} but the meter includes only ${included.toLocaleString()}, so ${plural(excess, unit)} ${excess === 1 ? "is" : "are"} billed as overage while still reported as allowed.`
      : `Permits ${plural(limit, unit)} but the meter blocks at ${included.toLocaleString()}, so the last ${plural(excess, unit)} can never be used.`;
  }

  return `Permits only ${plural(limit, unit)} but the meter includes ${included.toLocaleString()}, so ${plural(included - limit, unit)} of the allowance can never be used.`;
};
