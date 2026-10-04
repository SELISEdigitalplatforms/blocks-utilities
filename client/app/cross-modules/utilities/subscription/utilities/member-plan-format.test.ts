import { describe, expect, it } from "vitest";
import type { PlanSummaryData } from "../components/plan-summary-card";
import { describePace, describePlaces } from "./member-plan-format";

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
      subLimits: [
        { window: "Hour", windowCount: 1, rolling: false, quantity: 1_000_000, behaviour: "Refuse" },
      ],
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
      "One place per place bought (5 by default); 10,000,000 tokens each, at most 1,000,000 an hour, then refused.",
    );
  });

  /** Each part already uses commas, so the meters are told apart with semicolons. */
  it("keeps two meters' allowances apart in the sentence", () => {
    const twoMeters = plan({
      prices: [price("place")],
      meters: [
        { meterKey: "tokens", displayName: "Tokens", unitLabel: "token", includedQuantity: 100, overageAllowed: false },
        { meterKey: "calls", displayName: "Calls", unitLabel: "call", includedQuantity: 100, overageAllowed: false },
      ],
    });

    expect(describePlaces(twoMeters)).toBe(
      "One place per place bought (5 by default); 100 tokens each; 100 calls each.",
    );
  });

  it("counts places from the maximum when priced flat", () => {
    expect(describePlaces(plan({ prices: [price(null)] }))).toMatch(/^10 places at the flat price; /);
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

describe("describePace", () => {
  type Limit = NonNullable<PlanSummaryData["meters"][number]["subLimits"]>[number];

  const limit = (overrides: Partial<Limit> = {}): Limit => ({
    window: "Hour",
    windowCount: 1,
    rolling: false,
    quantity: 1_000,
    behaviour: "Refuse",
    ...overrides,
  });

  const meter = (...subLimits: Limit[]) => ({
    meterKey: "tokens",
    displayName: "Tokens",
    unitLabel: "token",
    includedQuantity: 1_000_000,
    overageAllowed: false,
    subLimits,
  });

  it("says nothing for a meter with no limit", () => {
    expect(describePace(meter())).toBeNull();
  });

  it("reads a one-hour fixed pace as per hour", () => {
    expect(describePace(meter(limit()))).toBe("at most 1,000 an hour, then refused");
  });

  /**
   * Dropping the count describes a cap five times tighter than the one sold — the plan's own
   * review step told its author "1,000 an hour" for a pace of 1,000 every 5 hours.
   */
  it("names every window a fixed pace spans", () => {
    expect(describePace(meter(limit({ windowCount: 5 })))).toBe(
      "at most 1,000 every 5 hours, then refused",
    );
  });

  it("says a rolling pace looks back over any stretch of that length", () => {
    expect(describePace(meter(limit({ windowCount: 5, rolling: true })))).toBe(
      "at most 1,000 in any 5 hours, then refused",
    );
    expect(describePace(meter(limit({ rolling: true })))).toBe(
      "at most 1,000 in any hour, then refused",
    );
  });

  it("names every limit, with the outcome said once when they all share it", () => {
    expect(
      describePace(
        meter(
          limit({ windowCount: 5, rolling: true }),
          limit({ window: "Week", quantity: 20_000 }),
        ),
      ),
    ).toBe("at most 1,000 in any 5 hours and 20,000 a week, then refused");
  });

  /** Refuse and report are opposites to whoever is spending, so each says which it is. */
  it("says each limit's own outcome when they differ", () => {
    expect(
      describePace(
        meter(
          limit({ windowCount: 5, rolling: true, behaviour: "Throttle" }),
          limit({ window: "Week", quantity: 20_000 }),
        ),
      ),
    ).toBe(
      "at most 1,000 in any 5 hours (then reported as over pace) and 20,000 a week (then refused)",
    );
  });
});
