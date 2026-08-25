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
  taxRateBasisPoints?: number | null;
  taxMode?: string | null;
  automaticDiscountBasisPoints?: number | null;
}): string => {
  const amount = formatMoney(price.unitAmountMinor, price.currencyCode);
  const cadence = formatInterval(price.interval, price.intervalCount);
  const base = price.quantityItemKey
    ? `${amount} per ${price.quantityItemKey}, ${cadence}`
    : `${amount} ${cadence}`;

  // Named beside the amount, because a price with 8% off it is not the price the amount says. The
  // combination is deliberately not named here: it only matters where there is also a volume band,
  // and this string appears in lists where a reader has no quantity in mind.
  const discounted = price.automaticDiscountBasisPoints
    ? `${base}, ${price.automaticDiscountBasisPoints / 100}% off`
    : base;

  // Stated wherever a price is shown, because the number alone does not say whether the customer
  // pays it or pays more than it. A legacy price carrying a rate and no mode is exclusive, which is
  // how the server charges it.
  if (!price.taxRateBasisPoints) {
    return discounted;
  }

  const rate = `${price.taxRateBasisPoints / 100}%`;

  return price.taxMode === "Inclusive"
    ? `${discounted} (incl. ${rate} tax)`
    : `${discounted} + ${rate} tax`;
};

export const formatMeterAllowance = (meter: {
  displayName: string;
  unitLabel: string;
  includedQuantity: number;
  overageAllowed: boolean;
  /** Empty or absent means the rater prices overage at zero. */
  rateTables?: { currencyCode: string }[];
}): string => {
  const included = `${meter.includedQuantity.toLocaleString()} ${meter.unitLabel}${meter.includedQuantity === 1 ? "" : "s"} included`;

  if (!meter.overageAllowed) {
    return `${included}, then blocked`;
  }

  // Saying "then overage billed" with no rate table would promise revenue that is never
  // charged: the rater cannot price a meter it has no tiers for, so it bills nothing. Worded
  // as a plain statement of what happens rather than "unlimited free", which read as a feature
  // being advertised instead of an allowance that caps nothing.
  return meter.rateTables?.length
    ? `${included}, then overage billed`
    : `${included}, then unlimited at no charge`;
};

/**
 * What one meter allows during the trial.
 *
 * A grant *replaces* the plan's allowance rather than adding to it, and a meter with no grant
 * keeps its full monthly one — which is exactly the case worth spelling out, since a trial that
 * hands out a whole month of something costly is an invitation to sign up, consume and leave.
 */
export const formatTrialAllowance = (
  meter: { displayName: string; unitLabel: string; includedQuantity: number },
  grant: { includedQuantity: number } | undefined,
): string => {
  const unit = meter.unitLabel || "unit";
  const plural = (count: number) =>
    `${count.toLocaleString()} ${unit}${count === 1 ? "" : "s"}`;

  if (!grant) {
    return `${plural(meter.includedQuantity)} — the full monthly allowance, with no separate trial limit`;
  }

  return grant.includedQuantity === meter.includedQuantity
    ? plural(grant.includedQuantity)
    : `${plural(grant.includedQuantity)}, instead of the usual ${meter.includedQuantity.toLocaleString()}`;
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
