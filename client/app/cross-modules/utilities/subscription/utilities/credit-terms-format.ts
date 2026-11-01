import { joinList, tSubscription } from "./subscription-i18n";
import { formatMoney } from "./subscription-format";

/** A meter's purchasable credits, as the plan summary reads them. */
export interface CreditTermsSummary {
  meterKey: string;
  enabled: boolean;
  unitPrices: { currencyCode: string; unitAmountMinor: number }[];
  minQuantity: number;
  /** Zero or absent while the author has not set one yet. */
  maxQuantity?: number | null;
  /** The form holds the number, a stored plan the name. */
  expiryPolicy: number | string;
  /** A fixed lifetime from purchase; the form holds the unit's number, a stored plan its name. */
  expiresAfter?: { count: number; unit: number | string } | null;
  taxRateBasisPoints?: number | null;
  taxMode?: string | null;
}

const lifetimeUnit = (unit: number | string, count: number): string => {
  const values = { count, quantity: count.toLocaleString() };

  switch (String(unit)) {
    case "0":
    case "Hour":
      return tSubscription("CREDIT_TERMS_FORMAT.LIFETIME_HOURS", values);
    case "2":
    case "Week":
      return tSubscription("CREDIT_TERMS_FORMAT.LIFETIME_WEEKS", values);
    case "3":
    case "Month":
      return tSubscription("CREDIT_TERMS_FORMAT.LIFETIME_MONTHS", values);
    case "4":
    case "Year":
      return tSubscription("CREDIT_TERMS_FORMAT.LIFETIME_YEARS", values);
    default:
      return tSubscription("CREDIT_TERMS_FORMAT.LIFETIME_DAYS", values);
  }
};

/** "30 days", "1 month": a fixed credit lifetime as a reader would say it. */
export const describeCreditLifetime = (lifetime: { count: number; unit: number | string }): string =>
  lifetimeUnit(lifetime.unit, lifetime.count);

const describeExpiryPolicy = (policy: number | string): string => {
  switch (String(policy)) {
    case "2":
    case "CarryForward":
      return tSubscription("CREDIT_TERMS_FORMAT.EXPIRY_CARRY_FORWARD");
    case "1":
    case "Never":
      return tSubscription("CREDIT_TERMS_FORMAT.EXPIRY_NEVER");
    default:
      return tSubscription("CREDIT_TERMS_FORMAT.EXPIRY_PERIODIC");
  }
};

/**
 * One meter's credit terms in a line: price, purchase bounds, expiry and tax. Paused terms say so
 * first, because a reviewer reading a price for credits nobody can buy would assume they can.
 */
export const describeCreditTerms = (credit: CreditTermsSummary): string => {
  const prices = credit.unitPrices.length
    ? joinList(
        credit.unitPrices.map((price) => formatMoney(price.unitAmountMinor, price.currencyCode)),
        "disjunction",
      )
    : tSubscription("CREDIT_TERMS_FORMAT.NO_PRICE_YET");

  const bounds = credit.maxQuantity
    ? tSubscription("CREDIT_TERMS_FORMAT.BOUNDS_RANGE", {
        min: credit.minQuantity.toLocaleString(),
        max: credit.maxQuantity.toLocaleString(),
      })
    : tSubscription("CREDIT_TERMS_FORMAT.BOUNDS_FROM", {
        min: credit.minQuantity.toLocaleString(),
      });

  const tax = credit.taxRateBasisPoints
    ? tSubscription(
        credit.taxMode === "Inclusive"
          ? "CREDIT_TERMS_FORMAT.TAX_INCLUDED"
          : "CREDIT_TERMS_FORMAT.TAX_ADDED",
        { rate: credit.taxRateBasisPoints / 100 },
      )
    : null;

  const parts = [
    tSubscription("CREDIT_TERMS_FORMAT.PRICE_PER_CREDIT", { prices }),
    bounds,
    credit.expiresAfter
      ? tSubscription("CREDIT_TERMS_FORMAT.CREDITS_LAST", {
          lifetime: describeCreditLifetime(credit.expiresAfter),
        })
      : describeExpiryPolicy(credit.expiryPolicy),
    ...(tax ? [tax] : []),
  ];

  const terms = parts.join(" · ");

  return credit.enabled ? terms : tSubscription("CREDIT_TERMS_FORMAT.PAUSED", { terms });
};
