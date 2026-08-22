import type { SafeParseReturnType } from "zod";
import { describe, expect, it } from "vitest";
import {
  buildSubscriptionPlanSchema,
  createSubscriptionPlanSchema,
  defaultSubscriptionPlanFormValues,
} from "./subscription-plan.schema";
import { FLAT_FEE } from "./subscription-price.schema";
import { TENANT_WIDE_ORGANIZATION } from "../constants/subscription.constants";

const price = {
  currencyCode: "EUR",
  amount: 3,
  interval: 2,
  intervalCount: 1,
  quantityItemKey: FLAT_FEE,
};

const validPlan = {
  ...defaultSubscriptionPlanFormValues,
  code: "pro",
  displayName: "Pro",
  organizationId: TENANT_WIDE_ORGANIZATION,
};

const issuePaths = (result: SafeParseReturnType<unknown, unknown>) =>
  result.success ? [] : result.error.issues.map((issue) => issue.path);

describe("createSubscriptionPlanSchema", () => {
  it("accepts a minimal tenant-wide plan", () => {
    const result = createSubscriptionPlanSchema.safeParse(validPlan);

    expect(result.success).toBe(true);
  });

  it("rejects a plan code with characters the server would reject", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      code: "Pro Plan!",
    });

    expect(result.success).toBe(false);
  });

  it("requires an organization to be explicitly chosen", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      organizationId: "",
    });

    expect(result.success).toBe(false);
  });

  it("rejects malformed features JSON", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      featuresJson: "{not json",
    });

    expect(result.success).toBe(false);
  });

  it("accepts a features JSON object", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      featuresJson: '{"betaAccess": true}',
    });

    expect(result.success).toBe(true);
  });

  it("requires a counted entitlement to name both a limit and a meter", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [
        {
          meterKey: "api-calls",
          displayName: "API calls",
          unitLabel: "call",
          aggregation: 0,
          includedQuantity: 1000,
          overageAllowed: true,
          thresholdPercents: [],
          rateTables: [],
        },
      ],
      entitlements: [
        {
          key: "usage",
          limitKind: 1,
        },
      ],
    });

    expect(result.success).toBe(false);
  });

  it("accepts a counted entitlement that names an existing meter and limit", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [
        {
          meterKey: "api-calls",
          displayName: "API calls",
          unitLabel: "call",
          aggregation: 0,
          includedQuantity: 1000,
          overageAllowed: true,
          thresholdPercents: [],
          rateTables: [],
        },
      ],
      entitlements: [
        {
          key: "usage",
          limitKind: 1,
          limit: 1000,
          meterKey: "api-calls",
        },
      ],
    });

    expect(result.success).toBe(true);
  });

  it("rejects an entitlement naming a meter the plan does not define", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      entitlements: [
        {
          key: "usage",
          limitKind: 0,
          meterKey: "not-a-meter",
        },
      ],
    });

    expect(result.success).toBe(false);
  });

  it("rejects a trial grant naming a meter the plan does not define", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      trialDays: 14,
      trialGrants: [{ meterKey: "not-a-meter", includedQuantity: 10 }],
    });

    expect(result.success).toBe(false);
  });

  it("rejects a quantity item whose maximum sits below its minimum", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      quantityItems: [
        {
          itemKey: "seat",
          unitLabel: "seat",
          minQuantity: 5,
          maxQuantity: 1,
          defaultQuantity: 5,
        },
      ],
    });

    expect(result.success).toBe(false);
  });

  it("keeps enum fields as numbers rather than strings, matching the request body", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [
        {
          meterKey: "api-calls",
          displayName: "API calls",
          unitLabel: "call",
          aggregation: 0,
          includedQuantity: 1000,
          overageAllowed: true,
          thresholdPercents: [80, 100],
          rateTables: [],
        },
      ],
    });

    expect(result.success).toBe(true);
    if (result.success) {
      expect(typeof result.data.meters[0].aggregation).toBe("number");
    }
  });

  it("accepts lifetime capacity when overage is blocked", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [
        {
          meterKey: "storage",
          displayName: "Storage",
          unitLabel: "byte",
          aggregation: 0,
          resetPolicy: 1,
          includedQuantity: 5_368_709_120,
          overageAllowed: false,
          thresholdPercents: [80, 100],
          rateTables: [],
        },
      ],
    });

    expect(result.success).toBe(true);
  });

  it("rejects monthly overage pricing on lifetime capacity", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [
        {
          meterKey: "storage",
          displayName: "Storage",
          unitLabel: "byte",
          aggregation: 0,
          resetPolicy: 1,
          includedQuantity: 5_368_709_120,
          overageAllowed: true,
          thresholdPercents: [],
          rateTables: [],
        },
      ],
    });

    expect(result.success).toBe(false);
  });

  const carryForwardMeter = (overrides: Record<string, unknown> = {}) => ({
    meterKey: "tokens",
    displayName: "Tokens",
    unitLabel: "token",
    aggregation: 0,
    resetPolicy: 2,
    includedQuantity: 1_000_000,
    overageAllowed: false,
    thresholdPercents: [],
    rateTables: [],
    ...overrides,
  });

  it("accepts a carry-forward meter that caps what rolls in", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [carryForwardMeter({ carryForwardCap: 500_000 })],
    });

    expect(result.success).toBe(true);
  });

  it("refuses a carry-forward meter with no cap", () => {
    // Without a ceiling a dormant subscription banks allowance indefinitely, which is never
    // what was sold.
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [carryForwardMeter()],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["meters", 0, "carryForwardCap"]);
  });

  it("refuses a cap on a meter that does not carry forward", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      meters: [carryForwardMeter({ resetPolicy: 0, carryForwardCap: 500_000 })],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["meters", 0, "carryForwardCap"]);
  });

  it("refuses a plan with no price, which nobody could subscribe to", () => {
    const result = createSubscriptionPlanSchema.safeParse({ ...validPlan, prices: [] });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["prices"]);
  });

  it("allows a plan with no price when editing, where prices are added separately", () => {
    const result = buildSubscriptionPlanSchema({ requirePrice: false }).safeParse({
      ...validPlan,
      prices: [],
    });

    expect(result.success).toBe(true);
  });

  it("rejects a price charging for a quantity item the plan does not define", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      prices: [{ ...price, quantityItemKey: "seat" }],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["prices", 0, "quantityItemKey"]);
  });

  it("accepts a price charging for a quantity item the plan does define", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      quantityItems: [
        {
          itemKey: "seat",
          unitLabel: "seat",
          minQuantity: 1,
          defaultQuantity: 1,
        },
      ],
      prices: [{ ...price, quantityItemKey: "seat" }],
    });

    expect(result.success).toBe(true);
  });

  it("rejects two prices with identical terms, which the server would reject after creating the plan", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      prices: [price, { ...price, amount: 99 }],
    });

    expect(result.success).toBe(false);
    expect(issuePaths(result)).toContainEqual(["prices", 1, "currencyCode"]);
  });

  it("accepts a monthly and an annual price on the same plan", () => {
    const result = createSubscriptionPlanSchema.safeParse({
      ...validPlan,
      prices: [price, { ...price, interval: 3, amount: 30 }],
    });

    expect(result.success).toBe(true);
  });
});
