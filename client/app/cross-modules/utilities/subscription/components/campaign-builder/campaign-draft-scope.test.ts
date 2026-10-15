import { describe, expect, it } from "vitest";
import type { SubscriptionPlan } from "../../models/subscription-plan.model";
import {
  EMPTY_DRAFT,
  purchasableMeters,
  stepProblems,
  toCreateDiscountRequest,
  withScope,
  type CampaignDraft,
} from "./campaign-draft";

/**
 * Discount codes scoped to credit purchases (spec 002).
 *
 * Guards against authoring a code the server refuses only at save — a credit code with a duration,
 * a price restriction or a campaign kind, or one limited to a meter nobody sells credits for — and
 * against a subscription code quietly picking up a credit code's one-use flag.
 */
const plans = [
  {
    planId: "plan-1",
    code: "pro",
    displayName: "Pro",
    meters: [
      {
        meterKey: "ai-credit",
        displayName: "AI",
        unitLabel: "credit",
        aggregation: "Sum",
        includedQuantity: 100,
        overageAllowed: false,
        thresholdPercents: [],
        topUp: {
          enabled: true,
          unitPrices: [{ currencyCode: "USD", unitAmountMinor: 10 }],
          minQuantity: 1,
          maxQuantity: 1_000,
          expiryPolicy: "Periodic",
          taxRateBasisPoints: null,
          taxMode: null,
        },
      },
      {
        meterKey: "storage",
        displayName: "Storage",
        unitLabel: "GB",
        aggregation: "Sum",
        includedQuantity: 10,
        overageAllowed: false,
        thresholdPercents: [],
      },
    ],
    prices: [],
    entitlements: [],
  },
] as unknown as SubscriptionPlan[];

const creditDraft = (overrides: Partial<CampaignDraft> = {}): CampaignDraft => ({
  ...withScope({ ...EMPTY_DRAFT, code: "topup20", displayName: "Top-up" }, "CreditPurchase"),
  ...overrides,
});

describe("credit-purchase discount codes", () => {
  it("drops what a credit code cannot carry when the scope is switched to it", () => {
    const draft = withScope(
      {
        ...EMPTY_DRAFT,
        campaignKind: "FirstAnnualPeriod",
        durationPeriods: "3",
        priceIds: ["price-1"],
      },
      "CreditPurchase",
    );

    expect(draft.campaignKind).toBe("Standard");
    expect(draft.durationPeriods).toBe("");
    expect(draft.priceIds).toEqual([]);
  });

  it("drops the meters and the one-use flag when switched back to subscriptions", () => {
    const draft = withScope(
      creditDraft({ meterKeys: ["ai-credit"], oneUsePerOrganization: true }),
      "Subscription",
    );

    expect(draft.meterKeys).toEqual([]);
    expect(draft.oneUsePerOrganization).toBe(false);
  });

  it("offers only meters whose credits are on sale", () => {
    expect(purchasableMeters(creditDraft(), plans).map((meter) => meter.meterKey)).toEqual([
      "ai-credit",
    ]);
  });

  it("refuses a meter no plan sells credits for", () => {
    expect(stepProblems(3, creditDraft({ meterKeys: ["storage"] }), plans)).toContain(
      "No plan this code applies to sells credits for the meter 'storage'.",
    );
  });

  it("sends the scope, the meters and the one-use flag, and never a duration or a price", () => {
    const request = toCreateDiscountRequest(
      creditDraft({ meterKeys: ["ai-credit"], oneUsePerOrganization: true, durationPeriods: "3" }),
      undefined,
    );

    expect(request.scope).toBe("CreditPurchase");
    expect(request.applicableMeterKeys).toEqual(["ai-credit"]);
    expect(request.oneUsePerOrganization).toBe(true);
    expect(request.durationPeriods).toBeUndefined();
    expect(request.applicablePriceIds).toEqual([]);
    expect(request.campaignKind).toBeUndefined();
  });

  it("keeps a standard subscription code free of the one-use flag", () => {
    const request = toCreateDiscountRequest(
      { ...EMPTY_DRAFT, code: "launch", displayName: "Launch", oneUsePerOrganization: true },
      undefined,
    );

    expect(request.scope).toBe("Subscription");
    expect(request.oneUsePerOrganization).toBeUndefined();
    expect(request.applicableMeterKeys).toEqual([]);
  });
});
