import { describe, expect, it } from "vitest";
import type { SubscriptionPlan } from "../models/subscription-plan.model";
import { createSubscriptionPlanSchema } from "../schemas/subscription-plan.schema";
import { planToFormValues, toCreatePlanRequest } from "./plan-form-mapping";

/**
 * Authoring purchasable credits in the plan builder (spec 002, AC-30).
 *
 * Guards against the console sending terms the server refuses only at save time — no ceiling on a
 * purchase, two prices in one currency — and against an edit quietly deleting credit terms it never
 * reopened, which would take credits off sale for every subscriber of the plan.
 */
const storedPlan = (topUp?: SubscriptionPlan["meters"][number]["topUp"]): SubscriptionPlan => ({
  planId: "plan-1",
  code: "pro",
  displayName: "Pro",
  description: null,
  featuresJson: null,
  organizationId: "org-1",
  trialDays: null,
  trialDurationKind: null,
  trialDurationCount: null,
  trialRequiresPaymentMethod: true,
  version: 1,
  hasSubscribers: false,
  quantityItems: [],
  meters: [
    {
      meterKey: "ai-credit",
      displayName: "AI",
      unitLabel: "credit",
      aggregation: "Sum",
      resetPolicy: "Periodic",
      includedQuantity: 100,
      overageAllowed: false,
      thresholdPercents: [],
      rateTables: [],
      topUp,
    },
  ],
  entitlements: [],
  prices: [
    {
      priceId: "price-1",
      currencyCode: "USD",
      unitAmountMinor: 1_000,
      interval: "Month",
      intervalCount: 1,
      quantityItemKey: null,
    },
  ],
  trialGrants: [],
});

const onSale = {
  enabled: true,
  unitPrices: [{ currencyCode: "USD", unitAmountMinor: 10 }],
  minQuantity: 10,
  maxQuantity: 1_000,
  expiryPolicy: "CarryForward" as const,
  taxRateBasisPoints: 810,
  taxMode: "Inclusive" as const,
};

const draft = (topUp?: SubscriptionPlan["meters"][number]["topUp"]) =>
  planToFormValues(storedPlan(topUp), { includePrices: true });

describe("meter credits in the plan builder", () => {
  it("reopens stored credit terms in the currency's own units", () => {
    const [credit] = draft(onSale).meterCredits;

    expect(credit.prices).toEqual([{ currencyCode: "USD", unitAmount: 0.1 }]);
    expect(credit.expiryPolicy).toBe(2);
    expect(credit.taxPercent).toBe(8.1);
    expect(credit.taxMode).toBe("Inclusive");
  });

  it("sends the terms back on the meter they belong to, in minor units and basis points", () => {
    const request = toCreatePlanRequest(createSubscriptionPlanSchema.parse(draft(onSale)));

    expect(request.meters[0].topUp).toEqual({
      enabled: true,
      unitPrices: [{ currencyCode: "USD", unitAmountMinor: 10 }],
      minQuantity: 10,
      maxQuantity: 1_000,
      expiryPolicy: 2,
      taxRateBasisPoints: 810,
      taxMode: "Inclusive",
    });
  });

  it("keeps paused terms, which an edit would otherwise delete", () => {
    const request = toCreatePlanRequest(
      createSubscriptionPlanSchema.parse(draft({ ...onSale, enabled: false })),
    );

    expect(request.meters[0].topUp?.enabled).toBe(false);
  });

  it("sends nothing for a meter whose credits were never set up", () => {
    const request = toCreatePlanRequest(createSubscriptionPlanSchema.parse(draft()));

    expect(request.meters[0].topUp).toBeUndefined();
  });

  it("refuses credits on sale without a largest purchase", () => {
    const values = draft(onSale);
    values.meterCredits[0].maxQuantity = undefined;

    const result = createSubscriptionPlanSchema.safeParse(values);

    expect(result.success).toBe(false);
    expect(result.error?.issues.map((issue) => issue.path.join("."))).toContain(
      "meterCredits.0.maxQuantity",
    );
  });

  it("saves paused credits even with half-written terms", () => {
    const values = draft({ ...onSale, enabled: false });
    values.meterCredits[0].maxQuantity = undefined;
    values.meterCredits[0].prices = [];

    expect(createSubscriptionPlanSchema.safeParse(values).success).toBe(true);
  });

  it("saves paused credits holding a price row not yet filled in, and sends no price for it", () => {
    const values = draft({ ...onSale, enabled: false });
    values.meterCredits[0].prices.push({ currencyCode: "", unitAmount: 0 });

    const result = createSubscriptionPlanSchema.safeParse(values);

    expect(result.success).toBe(true);
    expect(toCreatePlanRequest(result.data!).meters[0].topUp?.unitPrices).toEqual([
      { currencyCode: "USD", unitAmountMinor: 10 },
    ]);
  });

  it("still refuses a blank or free price on credits that are on sale", () => {
    const values = draft(onSale);
    values.meterCredits[0].prices.push({ currencyCode: "", unitAmount: 0 });

    const result = createSubscriptionPlanSchema.safeParse(values);

    expect(result.error?.issues.map((issue) => issue.path.join("."))).toEqual(
      expect.arrayContaining(["meterCredits.0.prices.1.currencyCode", "meterCredits.0.prices.1.unitAmount"]),
    );
  });

  it("refuses two prices in one currency", () => {
    const values = draft(onSale);
    values.meterCredits[0].prices.push({ currencyCode: "USD", unitAmount: 0.2 });

    expect(createSubscriptionPlanSchema.safeParse(values).success).toBe(false);
  });

  it("refuses credits for a meter the plan does not define", () => {
    const values = draft(onSale);
    values.meterCredits[0].meterKey = "storage";

    const result = createSubscriptionPlanSchema.safeParse(values);

    expect(result.error?.issues.map((issue) => issue.message)).toContain(
      "Choose a meter this plan defines.",
    );
  });

  it("refuses a second set of terms for the same meter", () => {
    const values = draft(onSale);
    values.meterCredits.push({ ...values.meterCredits[0] });

    expect(createSubscriptionPlanSchema.safeParse(values).success).toBe(false);
  });
});
