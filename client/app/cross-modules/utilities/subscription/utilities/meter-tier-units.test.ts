import { describe, expect, it } from "vitest";
import type { SubscriptionPlan } from "../models/subscription-plan.model";
import { createSubscriptionPlanSchema } from "../schemas/subscription-plan.schema";
import { planToFormValues, toUpdatePlanRequest } from "./plan-form-mapping";

const meter = (
  currencyCode: string,
  tiers: { upToQuantity: number | null; unitAmountMinor: number }[],
) => ({
  meterKey: "ses-signatures",
  displayName: "Simple signatures",
  unitLabel: "signature",
  aggregation: "Sum" as const,
  resetPolicy: "Periodic" as const,
  includedQuantity: 150,
  overageAllowed: true,
  thresholdPercents: [80],
  rateTables: [{ currencyCode, tiers }],
});

const storedPlan = (
  currencyCode: string,
  tiers: { upToQuantity: number | null; unitAmountMinor: number }[],
): SubscriptionPlan =>
  ({
    planId: "plan-1",
    code: "starter",
    displayName: "Starter",
    description: null,
    featuresJson: null,
    organizationId: "org-1",
    trialDays: null,
    trialRequiresPaymentMethod: true,
    version: 1,
    hasSubscribers: false,
    quantityItems: [],
    meters: [meter(currencyCode, tiers)],
    entitlements: [],
    prices: [],
    trialGrants: [],
  }) as unknown as SubscriptionPlan;

const formValuesWithTier = (currencyCode: string, unitAmount: number) => {
  const values = planToFormValues(
    storedPlan(currencyCode, [{ upToQuantity: null, unitAmountMinor: 0 }]),
  );

  values.meters[0].rateTables[0].tiers = [{ upToQuantity: undefined, unitAmount }];

  return values;
};

/**
 * A tier price is the figure most likely to be entered wrong by a factor of a hundred, because the
 * field used to ask for minor units while every other price on the page asked for real money. An
 * author pricing overage at five centimes and an author pricing it at five francs both typed 5.
 */
describe("meter tier prices", () => {
  describe("submitting", () => {
    it.each([
      ["CHF", 145, 14_500],
      ["USD", 0.05, 5],
      ["JPY", 100, 100],
      ["KWD", 1.25, 1_250],
    ])("sends %s %d as %i", (currency, typed, expected) => {
      const request = toUpdatePlanRequest(formValuesWithTier(currency, typed), "org-1");

      expect(request.meters[0].rateTables[0].tiers[0].unitAmountMinor).toBe(expected);
    });

    it("uses the table's own currency, not the plan's or the price's", () => {
      // Two tables on one meter, priced the same and stored differently. This is the case a single
      // shared exponent would get wrong.
      const values = formValuesWithTier("JPY", 100);

      values.meters[0].rateTables.push({
        currencyCode: "CHF",
        tiers: [{ upToQuantity: undefined, unitAmount: 100 }],
      });

      const tables = toUpdatePlanRequest(values, "org-1").meters[0].rateTables;

      expect(tables[0].tiers[0].unitAmountMinor).toBe(100);
      expect(tables[1].tiers[0].unitAmountMinor).toBe(10_000);
    });
  });

  describe("reopening", () => {
    it.each([
      ["CHF", 14_500, 145],
      ["USD", 5, 0.05],
      ["JPY", 100, 100],
      ["KWD", 1_250, 1.25],
    ])("reads a stored %s tier of %i back as %d", (currency, stored, expected) => {
      const values = planToFormValues(
        storedPlan(currency, [{ upToQuantity: null, unitAmountMinor: stored }]),
      );

      expect(values.meters[0].rateTables[0].tiers[0].unitAmount).toBe(expected);
    });

    it("does not move a stored figure when a plan is opened and saved unchanged", () => {
      // The regression that would matter most: editing a plan for an unrelated reason must not
      // reprice its overage.
      const stored = storedPlan("KWD", [
        { upToQuantity: 1_000, unitAmountMinor: 1_250 },
        { upToQuantity: null, unitAmountMinor: 3 },
      ]);

      const request = toUpdatePlanRequest(planToFormValues(stored), "org-1");

      expect(request.meters[0].rateTables[0].tiers).toEqual([
        { upToQuantity: 1_000, unitAmountMinor: 1_250 },
        { upToQuantity: undefined, unitAmountMinor: 3 },
      ]);
    });
  });

  describe("precision", () => {
    const parse = (currency: string, unitAmount: number) =>
      createSubscriptionPlanSchema.safeParse({
        ...formValuesWithTier(currency, unitAmount),
        prices: [
          {
            currencyCode: currency,
            amount: 1,
            interval: 2,
            intervalCount: 1,
            quantityItemKey: "__flat_fee__",
          },
        ],
      });

    it("accepts an amount the currency can express", () => {
      expect(parse("CHF", 0.05).success).toBe(true);
      expect(parse("KWD", 1.25).success).toBe(true);
      expect(parse("JPY", 100).success).toBe(true);
    });

    it("rejects a half-centime rather than rounding it up", () => {
      const result = parse("CHF", 0.005);

      expect(result.success).toBe(false);
      expect(JSON.stringify(result.error?.issues)).toContain("at most 2 decimal places");
    });

    it("rejects a fraction of a yen", () => {
      const result = parse("JPY", 100.5);

      expect(result.success).toBe(false);
      expect(JSON.stringify(result.error?.issues)).toContain("no decimal places");
    });

    it("rejects a fourth digit in a three-decimal currency", () => {
      expect(parse("KWD", 1.2345).success).toBe(false);
    });

    it("still allows a band priced at nothing", () => {
      // Overage recorded, permitted, billed nothing. Whether that was intended is the rate
      // table's business, not this field's.
      expect(parse("CHF", 0).success).toBe(true);
    });

    it("rejects a negative tier price", () => {
      expect(parse("CHF", -1).success).toBe(false);
    });
  });
});
