import { describe, expect, it } from "vitest";
import { describePriceProblem, toOfferRequest } from "./credit-offers-page";

/**
 * The credit offer dialog takes prices in the currency's own units. An author typing 0.5 means
 * fifty cents, not half a cent, and the request must carry minor units the API can store.
 */
const draft = (overrides: Partial<Parameters<typeof toOfferRequest>[0]> = {}): Parameters<typeof toOfferRequest>[0] => ({
  meterKey: "ai-credits",
  enabled: true,
  currencyCode: "USD",
  unitAmount: "0.10",
  minQuantity: "1",
  maxQuantity: "1000",
  expiryCount: "",
  expiryUnit: "never",
  taxRateBasisPoints: "",
  taxMode: "Exclusive",
  spendOrder: "SubscriptionFirst",
  packs: [{ code: "tc1", displayName: "Test Credit 1", quantity: "500", unitAmount: "0.5" }],
  ...overrides,
});

describe("credit offer prices", () => {
  it("converts the offer and pack prices from the currency's units to minor units", () => {
    const request = toOfferRequest(draft(), null);

    expect(request.unitPrices).toEqual([{ currencyCode: "USD", unitAmountMinor: 10 }]);
    expect(request.packs[0].unitPrices).toEqual([{ currencyCode: "USD", unitAmountMinor: 50 }]);
  });

  it("keeps a whole amount whole in a currency without minor units", () => {
    const request = toOfferRequest(draft({ currencyCode: "JPY", unitAmount: "5", packs: [] }), null);

    expect(request.unitPrices).toEqual([{ currencyCode: "JPY", unitAmountMinor: 5 }]);
  });

  it("refuses a price finer than the currency's minor unit, on the offer or on a pack", () => {
    expect(describePriceProblem(draft())).toBeNull();
    expect(describePriceProblem(draft({ unitAmount: "0.005" }))).not.toBeNull();
    expect(
      describePriceProblem(draft({ packs: [{ code: "p", displayName: "P", quantity: "1", unitAmount: "0.005" }] })),
    ).not.toBeNull();
    expect(describePriceProblem(draft({ unitAmount: "0" }))).not.toBeNull();
  });
});
