import { describe, expect, it } from "vitest";
import { parseOfflinePricing } from "./offline-pricing";

/**
 * The pricing an operator types is the amount every record invoice of the contract states, so a
 * wrong conversion here is a wrong figure on each document for the life of the subscription.
 */
describe("parseOfflinePricing", () => {
  it("sends the mode by number, since the server binds it that way", () => {
    expect(parseOfflinePricing("Catalogue", "", "CHF")).toEqual({ valid: true, pricing: { mode: 0 } });
    expect(parseOfflinePricing("None", "", "CHF")).toEqual({ valid: true, pricing: { mode: 2 } });
  });

  it("drops an amount left in the field when the mode is not a contract, which the server would refuse", () => {
    expect(parseOfflinePricing("None", "500", "CHF")).toEqual({ valid: true, pricing: { mode: 2 } });
  });

  it("converts a contract amount to minor units in the price's currency", () => {
    expect(parseOfflinePricing("Contract", "500.25", "CHF")).toEqual({
      valid: true,
      pricing: { mode: 1, contractAmountMinor: 50_025, contractCurrencyCode: "CHF" },
    });
    expect(parseOfflinePricing("Contract", "5000", "JPY")).toMatchObject({
      pricing: { contractAmountMinor: 5000 },
    });
  });

  it("refuses a missing, zero or negative contract amount", () => {
    for (const amount of ["", " ", "0", "-5", "abc"]) {
      expect(parseOfflinePricing("Contract", amount, "CHF")).toEqual({
        valid: false,
        errorKey: "OFFLINE_PRICING.CONTRACT_AMOUNT_REQUIRED",
      });
    }
  });

  it("refuses an amount finer than the currency's minor unit instead of rounding it silently", () => {
    expect(parseOfflinePricing("Contract", "500.005", "CHF")).toEqual({
      valid: false,
      errorKey: "OFFLINE_PRICING.CONTRACT_AMOUNT_PRECISION",
    });
  });
});
