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

const UNIT: Record<string, string> = {
  "0": "hour",
  Hour: "hour",
  "1": "day",
  Day: "day",
  "2": "week",
  Week: "week",
  "3": "month",
  Month: "month",
  "4": "year",
  Year: "year",
};

/** "30 days", "1 month": a fixed credit lifetime as a reader would say it. */
export const describeCreditLifetime = (lifetime: { count: number; unit: number | string }): string => {
  const unit = UNIT[String(lifetime.unit)] ?? "day";
  return `${lifetime.count.toLocaleString()} ${unit}${lifetime.count === 1 ? "" : "s"}`;
};

const EXPIRY: Record<string, string> = {
  "0": "expire with the usage window",
  Periodic: "expire with the usage window",
  "2": "carry forward until the plan changes",
  CarryForward: "carry forward until the plan changes",
  "1": "never expire",
  Never: "never expire",
};

/**
 * One meter's credit terms in a line: price, purchase bounds, expiry and tax. Paused terms say so
 * first, because a reviewer reading a price for credits nobody can buy would assume they can.
 */
export const describeCreditTerms = (credit: CreditTermsSummary): string => {
  const prices = credit.unitPrices.length
    ? credit.unitPrices
        .map((price) => formatMoney(price.unitAmountMinor, price.currencyCode))
        .join(" or ")
    : "no price yet";

  const bounds = credit.maxQuantity
    ? `${credit.minQuantity.toLocaleString()}–${credit.maxQuantity.toLocaleString()} per purchase`
    : `from ${credit.minQuantity.toLocaleString()} per purchase, no maximum set`;

  const tax = credit.taxRateBasisPoints
    ? `${credit.taxRateBasisPoints / 100}% tax ${credit.taxMode === "Inclusive" ? "included" : "added"}`
    : null;

  const parts = [
    `${prices} per credit`,
    bounds,
    credit.expiresAfter
      ? `credits last ${describeCreditLifetime(credit.expiresAfter)} from purchase`
      : `credits ${EXPIRY[String(credit.expiryPolicy)] ?? "expire with the usage window"}`,
    ...(tax ? [tax] : []),
  ];

  return `${credit.enabled ? "" : "Paused · "}${parts.join(" · ")}`;
};
