import { describe, expect, it, vi } from "vitest";
import type { SubscriptionPlan } from "../models/subscription-plan.model";
import { FLAT_FEE } from "../schemas/subscription-price.schema";
import { submitPlanWithPrices } from "./submit-plan-with-prices";

const planRequest = {
  code: "starter",
  displayName: "Starter",
  trialRequiresPaymentMethod: true,
  quantityItems: [],
  meters: [],
  entitlements: [],
  trialGrants: [],
};

const createdPlan = (organizationId: string | null = null) =>
  ({
    planId: "plan-1",
    displayName: "Starter",
    organizationId,
  }) as SubscriptionPlan;

const price = (overrides: Partial<Parameters<typeof submitPlanWithPrices>[0]["prices"][number]> = {}) => ({
  currencyCode: "EUR",
  amount: 3,
  interval: 2,
  intervalCount: 1,
  quantityItemKey: FLAT_FEE,
  ...overrides,
});

describe("submitPlanWithPrices", () => {
  it("creates the plan first, then a price per row", async () => {
    const createPlan = vi.fn().mockResolvedValue(createdPlan());
    const createPrice = vi.fn().mockResolvedValue(createdPlan());

    const result = await submitPlanWithPrices({
      planRequest,
      prices: [price(), price({ interval: 3, amount: 30 })],
      createPlan,
      createPrice,
    });

    expect(createPlan).toHaveBeenCalledOnce();
    expect(createPrice).toHaveBeenCalledTimes(2);
    expect(createPrice.mock.calls[0][0]).toMatchObject({ planId: "plan-1", interval: 2 });
    expect(createPrice.mock.calls[1][0]).toMatchObject({ interval: 3 });
    expect(result.failures).toEqual([]);
  });

  it("converts the amount to minor units and drops the flat-fee sentinel", async () => {
    const createPrice = vi.fn().mockResolvedValue(createdPlan());

    await submitPlanWithPrices({
      planRequest,
      prices: [price({ amount: 3.5 })],
      createPlan: vi.fn().mockResolvedValue(createdPlan()),
      createPrice,
    });

    expect(createPrice.mock.calls[0][0]).toMatchObject({
      unitAmountMinor: 350,
      quantityItemKey: undefined,
    });
  });

  it("sends an automatic discount with its combination, or neither", async () => {
    const createPrice = vi.fn().mockResolvedValue(createdPlan());

    await submitPlanWithPrices({
      planRequest,
      prices: [
        price({ automaticDiscountPercent: 8, quantityDiscountCombination: "Additive" }),
        // No discount, but a combination left over from the form's default. Sending it would
        // describe how a reduction that does not exist meets a band.
        price({ quantityDiscountCombination: "Additive" }),
      ],
      createPlan: vi.fn().mockResolvedValue(createdPlan()),
      createPrice,
    });

    expect(createPrice.mock.calls[0][0]).toMatchObject({
      automaticDiscountBasisPoints: 800,
      quantityDiscountCombination: "Additive",
    });
    expect(createPrice.mock.calls[1][0]).toMatchObject({
      automaticDiscountBasisPoints: undefined,
      quantityDiscountCombination: undefined,
    });
  });

  it("names the plan's organization on every price", async () => {
    const createPrice = vi.fn().mockResolvedValue(createdPlan("org-x"));

    await submitPlanWithPrices({
      planRequest,
      prices: [price()],
      createPlan: vi.fn().mockResolvedValue(createdPlan("org-x")),
      createPrice,
    });

    expect(createPrice.mock.calls[0][0]).toMatchObject({ organizationId: "org-x" });
  });

  it("keeps the created plan and reports the prices that failed", async () => {
    const createPrice = vi
      .fn()
      .mockResolvedValueOnce(createdPlan())
      .mockRejectedValueOnce(new Error("A price with these terms already exists."));

    const result = await submitPlanWithPrices({
      planRequest,
      prices: [price(), price({ amount: 30 })],
      createPlan: vi.fn().mockResolvedValue(createdPlan()),
      createPrice,
    });

    expect(result.plan.planId).toBe("plan-1");
    expect(result.failures).toHaveLength(1);
    expect(result.failures[0]).toContain("Price 2");
    expect(result.failures[0]).toContain("A price with these terms already exists.");
  });

  it("carries on after a failing price so a later one still lands", async () => {
    const createPrice = vi
      .fn()
      .mockRejectedValueOnce(new Error("nope"))
      .mockResolvedValueOnce(createdPlan());

    const result = await submitPlanWithPrices({
      planRequest,
      prices: [price(), price({ amount: 30 })],
      createPlan: vi.fn().mockResolvedValue(createdPlan()),
      createPrice,
    });

    expect(createPrice).toHaveBeenCalledTimes(2);
    expect(result.failures).toHaveLength(1);
  });

  it("throws without pricing anything when the plan itself fails", async () => {
    const createPrice = vi.fn();

    await expect(
      submitPlanWithPrices({
        planRequest,
        prices: [price()],
        createPlan: vi.fn().mockRejectedValue(new Error("A plan with this code already exists.")),
        createPrice,
      }),
    ).rejects.toThrow("A plan with this code already exists.");

    expect(createPrice).not.toHaveBeenCalled();
  });
});
