import {
  formatMoney,
  isRepresentableInMinorUnits,
  toMinorUnits,
} from "../../subscription/utilities/subscription-format";
import {
  OFFLINE_PRICING_MODE_VALUE,
  type OfflinePricingMode,
  type OfflinePricingRequest,
} from "../models/subscription-simulation.model";

export type OfflinePricingParse =
  | { valid: true; pricing: OfflinePricingRequest }
  | { valid: false; errorKey: "OFFLINE_PRICING.CONTRACT_AMOUNT_REQUIRED" | "OFFLINE_PRICING.CONTRACT_AMOUNT_PRECISION" };

/**
 * The operator's pricing choice as the server binds it.
 *
 * The amount is typed in major units and sent in minor ones, and only a contract carries one: the
 * server refuses an amount or currency on any other mode, so they are dropped rather than sent.
 * The currency is the price's own, since a contract invoiced in another currency than the plan's
 * price would make every document disagree with its own tax and rounding.
 */
export const parseOfflinePricing = (
  mode: OfflinePricingMode,
  contractAmount: string,
  currencyCode: string,
): OfflinePricingParse => {
  if (mode !== "Contract") {
    return { valid: true, pricing: { mode: OFFLINE_PRICING_MODE_VALUE[mode] } };
  }

  const amount = Number(contractAmount);
  if (!contractAmount.trim() || !Number.isFinite(amount) || amount <= 0) {
    return { valid: false, errorKey: "OFFLINE_PRICING.CONTRACT_AMOUNT_REQUIRED" };
  }

  if (!isRepresentableInMinorUnits(amount, currencyCode)) {
    return { valid: false, errorKey: "OFFLINE_PRICING.CONTRACT_AMOUNT_PRECISION" };
  }

  return {
    valid: true,
    pricing: {
      mode: OFFLINE_PRICING_MODE_VALUE.Contract,
      contractAmountMinor: toMinorUnits(amount, currencyCode),
      contractCurrencyCode: currencyCode,
    },
  };
};

/**
 * One line naming how an offline subscription is priced, for the customer card and the operator
 * page alike. A contract shows its amount; no-price says so rather than showing a zero, because a
 * zero reads as "free" and the customer pays under an agreement this system never sees.
 */
export const describeOfflinePricing = (
  t: (key: string, params?: Record<string, unknown>) => string,
  mode: OfflinePricingMode | null | undefined,
  contractAmountMinor: number | null | undefined,
  currencyCode: string,
): string => {
  if (mode === "Contract" && contractAmountMinor != null) {
    return t("OFFLINE_PRICING.SUMMARY_CONTRACT", { amount: formatMoney(contractAmountMinor, currencyCode) });
  }
  return t(mode === "None" ? "OFFLINE_PRICING.SUMMARY_NONE" : "OFFLINE_PRICING.SUMMARY_CATALOGUE");
};
