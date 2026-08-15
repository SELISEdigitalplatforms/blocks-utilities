/**
 * Minor-unit exponent per currency, matching what the server's currency resolver accepts
 * (SUBSCRIPTION_CURRENCY_OPTIONS). Defaults to 2 for anything not listed, which covers every
 * common currency; only three-decimal currencies like BHD/KWD need calling out.
 */
const MINOR_UNIT_EXPONENT: Record<string, number> = {
  BHD: 3,
  KWD: 3,
  JPY: 0,
};

export const minorUnitExponent = (currencyCode: string): number =>
  MINOR_UNIT_EXPONENT[currencyCode.toUpperCase()] ?? 2;

export const toMinorUnits = (majorAmount: number, currencyCode: string): number =>
  Math.round(majorAmount * 10 ** minorUnitExponent(currencyCode));

export const toMajorUnits = (minorAmount: number, currencyCode: string): number =>
  minorAmount / 10 ** minorUnitExponent(currencyCode);

export const formatMoney = (unitAmountMinor: number, currencyCode: string): string => {
  try {
    return new Intl.NumberFormat(undefined, {
      style: "currency",
      currency: currencyCode.toUpperCase(),
    }).format(toMajorUnits(unitAmountMinor, currencyCode));
  } catch {
    return `${toMajorUnits(unitAmountMinor, currencyCode).toFixed(minorUnitExponent(currencyCode))} ${currencyCode.toUpperCase()}`;
  }
};

const INTERVAL_LABEL: Record<string, string> = {
  Day: "day",
  Week: "week",
  Month: "month",
  Year: "year",
};

export const formatInterval = (interval: string, intervalCount: number): string => {
  const unit = INTERVAL_LABEL[interval] ?? interval.toLowerCase();

  return intervalCount === 1 ? `every ${unit}` : `every ${intervalCount} ${unit}s`;
};

export const formatPrice = (price: {
  currencyCode: string;
  unitAmountMinor: number;
  interval: string;
  intervalCount: number;
  quantityItemKey: string | null;
}): string => {
  const amount = formatMoney(price.unitAmountMinor, price.currencyCode);
  const cadence = formatInterval(price.interval, price.intervalCount);

  return price.quantityItemKey
    ? `${amount} per ${price.quantityItemKey}, ${cadence}`
    : `${amount} ${cadence}`;
};

export const formatMeterAllowance = (meter: {
  displayName: string;
  unitLabel: string;
  includedQuantity: number;
  overageAllowed: boolean;
}): string => {
  const included = `${meter.includedQuantity.toLocaleString()} ${meter.unitLabel}${meter.includedQuantity === 1 ? "" : "s"} included`;

  return meter.overageAllowed ? `${included}, then overage billed` : `${included}, then blocked`;
};

export const formatEntitlementLimit = (entitlement: {
  limitKind: string;
  limit: number | null;
  unitLabel: string | null;
}): string => {
  if (entitlement.limitKind === "Unlimited") {
    return "Unlimited";
  }

  if (entitlement.limitKind === "Boolean") {
    return "Granted";
  }

  const unit = entitlement.unitLabel ? ` ${entitlement.unitLabel}` : "";

  return `Up to ${(entitlement.limit ?? 0).toLocaleString()}${unit}`;
};
