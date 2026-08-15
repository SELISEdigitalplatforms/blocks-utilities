import { describe, expect, it } from "vitest";
import {
  createSubscriptionPlanSchema,
  defaultSubscriptionPlanFormValues,
} from "./subscription-plan.schema";
import { TENANT_WIDE_ORGANIZATION } from "../constants/subscription.constants";

const validPlan = {
  ...defaultSubscriptionPlanFormValues,
  code: "pro",
  displayName: "Pro",
  organizationId: TENANT_WIDE_ORGANIZATION,
};

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
});
