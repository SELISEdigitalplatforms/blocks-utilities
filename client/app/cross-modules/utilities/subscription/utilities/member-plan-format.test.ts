import { describe, expect, it } from "vitest";
import type { PlanSummaryData } from "../components/plan-summary-card";
import { describePlaces } from "./member-plan-format";

const plan = (overrides: Partial<PlanSummaryData> = {}): PlanSummaryData => ({
  displayName: "AI",
  code: "ai",
  organizationLabel: "Tenant-wide",
  trialDurationKind: null,
  trialDurationCount: null,
  trialRequiresPaymentMethod: false,
  subscriberScope: "User",
  quantityItems: [{ itemKey: "place", unitLabel: "place", defaultQuantity: 5, maxQuantity: 10 }],
  meters: [
    {
      meterKey: "tokens",
      displayName: "Tokens",
      unitLabel: "token",
      includedQuantity: 10_000_000,
      overageAllowed: false,
      subLimitWindow: "Hour",
      subLimitQuantity: 1_000_000,
      subLimitBehaviour: "Refuse",
    },
  ],
  entitlements: [],
  prices: [],
  trialGrants: [],
  ...overrides,
});

const price = (quantityItemKey: string | null) => ({
  currencyCode: "CHF",
  unitAmountMinor: 1000,
  interval: "Month",
  intervalCount: 1,
  quantityItemKey,
});

describe("describePlaces", () => {
  it("counts places from the quantity bought when priced per place", () => {
    expect(describePlaces(plan({ prices: [price("place")] }))).toBe(
      "One place per place bought (5 by default), 10,000,000 tokens each, at most 1,000,000 an hour, then refused.",
    );
  });

  it("counts places from the maximum when priced flat", () => {
    expect(describePlaces(plan({ prices: [price(null)] }))).toMatch(/^10 places at the flat price, /);
  });

  it("says a flat price with no maximum gives no places", () => {
    const noMax = plan({
      prices: [price(null)],
      quantityItems: [{ itemKey: "place", unitLabel: "place", defaultQuantity: 5, maxQuantity: null }],
    });

    expect(describePlaces(noMax)).toMatch(/^No maximum set/);
  });

  it("reads a plan with no quantity as one person's", () => {
    expect(describePlaces(plan({ quantityItems: [], meters: [] }))).toBe("One place.");
  });

  it("asks for the mark when two quantities leave it ambiguous", () => {
    const two = plan({
      quantityItems: [
        { itemKey: "place", unitLabel: "place", defaultQuantity: 1, maxQuantity: 5 },
        { itemKey: "workspace", unitLabel: "workspace", defaultQuantity: 1, maxQuantity: 5 },
      ],
    });

    expect(describePlaces(two)).toMatch(/^Mark which quantity counts people/);
  });
});
