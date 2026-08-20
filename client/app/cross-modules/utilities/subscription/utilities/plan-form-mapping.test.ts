import { describe, expect, it } from "vitest";
import { TENANT_WIDE_ORGANIZATION } from "../constants/subscription.constants";
import type { SubscriptionPlan } from "../models/subscription-plan.model";
import { createSubscriptionPlanSchema } from "../schemas/subscription-plan.schema";
import { planToFormValues, toUpdatePlanRequest } from "./plan-form-mapping";

const storedPlan = (overrides: Partial<SubscriptionPlan> = {}): SubscriptionPlan => ({
  planId: "plan-1",
  code: "starter",
  displayName: "Starter",
  description: "For small teams",
  featuresJson: null,
  organizationId: "org-1",
  trialDays: 1,
  trialRequiresPaymentMethod: true,
  version: 1,
  hasSubscribers: false,
  quantityItems: [
    {
      itemKey: "seat",
      unitLabel: "seat",
      minQuantity: 1,
      maxQuantity: 50,
      defaultQuantity: 1,
    },
  ],
  meters: [
    {
      meterKey: "ses-signatures",
      displayName: "Simple Signatures",
      unitLabel: "signature",
      aggregation: "Sum",
      resetPolicy: "Periodic",
      includedQuantity: 150,
      overageAllowed: true,
      thresholdPercents: [80, 100],
      rateTables: [
        {
          currencyCode: "EUR",
          tiers: [
            { upToQuantity: 1000, unitAmountMinor: 5 },
            { upToQuantity: null, unitAmountMinor: 3 },
          ],
        },
      ],
    },
  ],
  entitlements: [
    {
      key: "ses-signatures",
      limitKind: "Count",
      limit: 150,
      meterKey: "ses-signatures",
      unitLabel: "signature",
    },
  ],
  prices: [
    {
      priceId: "price-1",
      currencyCode: "EUR",
      unitAmountMinor: 300,
      interval: "Month",
      intervalCount: 1,
      quantityItemKey: null,
    },
  ],
  trialGrants: [{ meterKey: "ses-signatures", includedQuantity: 5 }],
  ...overrides,
});

describe("planToFormValues", () => {
  it("turns response enum names back into the numbers the form and request use", () => {
    const values = planToFormValues(storedPlan());

    expect(values.meters[0].aggregation).toBe(0);
    expect(values.meters[0].resetPolicy).toBe(0);
    expect(values.entitlements[0].limitKind).toBe(1);
  });

  it("maps a never-reset response to the numeric request enum", () => {
    const plan = storedPlan();
    plan.meters[0] = {
      ...plan.meters[0],
      resetPolicy: "Never",
      overageAllowed: false,
      rateTables: [],
    };

    expect(planToFormValues(plan).meters[0].resetPolicy).toBe(1);
  });

  it("keeps trial grants, which an edit would otherwise drop", () => {
    expect(planToFormValues(storedPlan()).trialGrants).toEqual([
      { meterKey: "ses-signatures", includedQuantity: 5 },
    ]);
  });

  it("starts with no price rows, because saving does not touch existing prices", () => {
    expect(planToFormValues(storedPlan()).prices).toEqual([]);
  });

  it("reads a tenant-wide plan back as the sentinel the picker uses", () => {
    expect(planToFormValues(storedPlan({ organizationId: null })).organizationId).toBe(
      TENANT_WIDE_ORGANIZATION,
    );
  });

  it("carries an unbounded final tier across as undefined, not null", () => {
    const table = planToFormValues(storedPlan()).meters[0].rateTables[0];

    expect(table.tiers[1].upToQuantity).toBeUndefined();
  });

  it("produces a draft the create schema still accepts, apart from the price it now lacks", () => {
    const values = planToFormValues(storedPlan());
    const result = createSubscriptionPlanSchema.safeParse({
      ...values,
      prices: [
        {
          currencyCode: "EUR",
          amount: 3,
          interval: 2,
          intervalCount: 1,
          quantityItemKey: "seat",
        },
      ],
    });

    expect(result.success).toBe(true);
  });
});

describe("toUpdatePlanRequest", () => {
  it("names the plan's own organization so the server can find it", () => {
    const request = toUpdatePlanRequest(planToFormValues(storedPlan()), "org-1");

    expect(request.organizationId).toBe("org-1");
  });

  it("omits the organization for a tenant-wide plan", () => {
    const request = toUpdatePlanRequest(planToFormValues(storedPlan()), null);

    expect(request.organizationId).toBeUndefined();
  });

  it("sends no code, which the server takes from the stored plan", () => {
    const request = toUpdatePlanRequest(planToFormValues(storedPlan()), "org-1");

    expect(request).not.toHaveProperty("code");
  });

  it("round-trips a plan's terms unchanged when nothing was edited", () => {
    const request = toUpdatePlanRequest(planToFormValues(storedPlan()), "org-1");

    expect(request.displayName).toBe("Starter");
    expect(request.meters[0]).toMatchObject({
      meterKey: "ses-signatures",
      aggregation: 0,
      resetPolicy: 0,
      includedQuantity: 150,
      thresholdPercents: [80, 100],
    });
    expect(request.trialGrants).toEqual([{ meterKey: "ses-signatures", includedQuantity: 5 }]);
  });
});
