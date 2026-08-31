import { describe, expect, it } from "vitest";
import type { SubscriptionPlan } from "../../models/subscription-plan.model";
import {
  EMPTY_DRAFT,
  canSubmit,
  eligiblePrices,
  stepProblems,
  toCreateDiscountRequest,
  withCampaignKind,
  type CampaignDraft,
} from "./campaign-draft";

const plan = (overrides: Partial<SubscriptionPlan> = {}): SubscriptionPlan =>
  ({
    planId: "plan-1",
    code: "pro",
    displayName: "Pro",
    description: null,
    entitlements: [{ key: "seats", limitKind: "Count", limit: 5, meterKey: null, unitLabel: "seat" }],
    prices: [
      {
        priceId: "price-anniversary-month",
        currencyCode: "USD",
        unitAmountMinor: 1_000,
        interval: "Month",
        intervalCount: 1,
        billingAlignment: "Anniversary",
        quantityItemKey: null,
      },
      {
        priceId: "price-calendar-month",
        currencyCode: "USD",
        unitAmountMinor: 1_000,
        interval: "Month",
        intervalCount: 1,
        billingAlignment: "CalendarMonth",
        quantityItemKey: null,
      },
      {
        priceId: "price-calendar-year",
        currencyCode: "USD",
        unitAmountMinor: 10_000,
        interval: "Year",
        intervalCount: 1,
        billingAlignment: "CalendarMonth",
        calendarStubBasePriceId: "price-calendar-month",
        calendarStubBaseUnitAmountMinor: 1_000,
        quantityItemKey: null,
      },
      {
        priceId: "price-calendar-year-no-stub",
        currencyCode: "USD",
        unitAmountMinor: 10_000,
        interval: "Year",
        intervalCount: 1,
        billingAlignment: "CalendarMonth",
        quantityItemKey: null,
      },
      {
        priceId: "price-anniversary-year",
        currencyCode: "USD",
        unitAmountMinor: 10_000,
        interval: "Year",
        intervalCount: 1,
        billingAlignment: "Anniversary",
        quantityItemKey: null,
      },
    ],
    ...overrides,
  }) as unknown as SubscriptionPlan;

describe("withCampaignKind", () => {
  it("locks a free-opening-period campaign to a full 100% reduction and its two required flags", () => {
    const draft = withCampaignKind(
      { ...EMPTY_DRAFT, percent: "25" },
      "FreeOpeningCalendarPeriod",
    );

    expect(draft.discountKind).toBe("percent");
    expect(draft.percent).toBe("100");
    expect(draft.oneUsePerOrganization).toBe(true);
    expect(draft.requiresPaymentMethodUpfront).toBe(true);
  });

  it("clears any entitlement override when switching to first-annual-period", () => {
    const draft = withCampaignKind(
      { ...EMPTY_DRAFT, entitlementKey: "seats", entitlementLimit: "1" },
      "FirstAnnualPeriod",
    );

    expect(draft.entitlementKey).toBe("");
    expect(draft.entitlementLimit).toBe("");
  });

  it("leaves an ordinary field untouched when switching to Standard", () => {
    const draft = withCampaignKind({ ...EMPTY_DRAFT, percent: "25" }, "Standard");

    expect(draft.percent).toBe("25");
  });
});

describe("eligiblePrices", () => {
  const plans = [plan()];

  it("offers every price for a Standard discount", () => {
    expect(eligiblePrices("Standard", plans)).toHaveLength(5);
  });

  it("offers only calendar-aligned monthly prices for a free-opening-period campaign", () => {
    const result = eligiblePrices("FreeOpeningCalendarPeriod", plans);

    expect(result.map(({ price }) => price.priceId)).toEqual(["price-calendar-month"]);
  });

  it("offers only calendar-aligned yearly prices with a stub base for a first-annual-period campaign", () => {
    const result = eligiblePrices("FirstAnnualPeriod", plans);

    // Excludes the anniversary year and the calendar year with no stub base configured -- both
    // would silently mis-discount at redemption if this campaign kind were authored against them.
    expect(result.map(({ price }) => price.priceId)).toEqual(["price-calendar-year"]);
  });
});

describe("stepProblems", () => {
  const plans = [plan()];
  const validCampaign = (): CampaignDraft =>
    withCampaignKind(
      {
        ...EMPTY_DRAFT,
        code: "annual15",
        displayName: "15% off year one",
        priceIds: ["price-calendar-year"],
        validFromDate: "2026-01-01",
        validThroughDate: "2026-12-31",
      },
      "FirstAnnualPeriod",
    );

  it("step 1 refuses an empty code or display name", () => {
    expect(stepProblems(1, EMPTY_DRAFT, plans)).not.toHaveLength(0);
  });

  it("step 1 refuses a code with characters the server would reject", () => {
    const draft = { ...EMPTY_DRAFT, code: "Launch 25!", displayName: "Launch" };

    expect(stepProblems(1, draft, plans).some((problem) => problem.includes("lowercase"))).toBe(true);
  });

  it("step 1 accepts a valid code and display name", () => {
    const draft = { ...EMPTY_DRAFT, code: "launch-25", displayName: "Launch" };

    expect(stepProblems(1, draft, plans)).toHaveLength(0);
  });

  it("step 2 refuses a partial reduction for a free-opening-period campaign", () => {
    const draft = withCampaignKind({ ...EMPTY_DRAFT, percent: "100" }, "FreeOpeningCalendarPeriod");
    const partial = { ...draft, percent: "50" };

    expect(stepProblems(2, partial, plans).some((problem) => problem.includes("100%"))).toBe(true);
  });

  it("step 2 accepts a locked free-opening-period campaign", () => {
    const draft = withCampaignKind(EMPTY_DRAFT, "FreeOpeningCalendarPeriod");

    expect(stepProblems(2, draft, plans)).toHaveLength(0);
  });

  it("step 2 refuses a Standard discount whose expiry is not after its start", () => {
    const draft = {
      ...EMPTY_DRAFT,
      startsAtUtc: "2026-10-31T18:00",
      expiresAtUtc: "2026-10-01T09:30",
    };

    expect(stepProblems(2, draft, plans)).toContain("The discount must expire after it starts.");
  });

  it("step 3 refuses a campaign with no price named", () => {
    const draft = withCampaignKind(EMPTY_DRAFT, "FirstAnnualPeriod");

    expect(stepProblems(3, draft, plans).some((problem) => problem.includes("at least one price"))).toBe(
      true,
    );
  });

  it("step 3 does not require a price for a Standard discount", () => {
    expect(stepProblems(3, EMPTY_DRAFT, plans)).toHaveLength(0);
  });

  it("step 3 refuses a campaign window that ends before it starts", () => {
    const draft = { ...validCampaign(), validFromDate: "2026-12-31", validThroughDate: "2026-01-01" };

    expect(stepProblems(3, draft, plans).some((problem) => problem.includes("end before it starts"))).toBe(
      true,
    );
  });

  it("step 3 accepts a fully-filled first-annual-period campaign", () => {
    expect(stepProblems(3, validCampaign(), plans)).toHaveLength(0);
  });

  it("step 3 refuses a free-opening-period campaign with no entitlement named", () => {
    const draft = withCampaignKind(
      { ...EMPTY_DRAFT, priceIds: ["price-calendar-month"], validFromDate: "2026-1-1", validThroughDate: "2026-2-1" },
      "FreeOpeningCalendarPeriod",
    );

    expect(
      stepProblems(3, draft, plans).some((problem) => problem.includes("temporarily caps")),
    ).toBe(true);
  });

  it("step 3 refuses an entitlement key the plan does not grant", () => {
    const draft = withCampaignKind(
      {
        ...EMPTY_DRAFT,
        priceIds: ["price-calendar-month"],
        planCodes: ["pro"],
        validFromDate: "2026-1-1",
        validThroughDate: "2026-2-1",
        entitlementKey: "nonexistent",
        entitlementLimit: "1",
      },
      "FreeOpeningCalendarPeriod",
    );

    expect(stepProblems(3, draft, plans).some((problem) => problem.includes("nonexistent"))).toBe(true);
  });
});

describe("canSubmit", () => {
  const plans = [plan()];

  it("is false for the empty draft", () => {
    expect(canSubmit(EMPTY_DRAFT, plans)).toBe(false);
  });

  it("is true once every one of the first three steps is satisfied", () => {
    const draft: CampaignDraft = {
      ...EMPTY_DRAFT,
      code: "launch-25",
      displayName: "Launch offer",
    };

    expect(canSubmit(draft, plans)).toBe(true);
  });
});

describe("toCreateDiscountRequest", () => {
  it("sends an explicitly selected precedence for a Standard discount", () => {
    const draft: CampaignDraft = { ...EMPTY_DRAFT, code: "launch-25", displayName: "Launch" };

    const request = toCreateDiscountRequest(draft, "org-1");

    expect(request.campaignKind).toBeUndefined();
    expect(request.campaignPrecedence).toBe("BestDiscount");
    expect(request.validFromDate).toBeUndefined();
    expect(request.oneUsePerOrganization).toBeUndefined();
    expect(request.entitlementOverrideKey).toBeUndefined();
  });

  it("preserves the plan-policy behavior of a legacy Standard discount until an admin chooses", () => {
    const request = toCreateDiscountRequest(
      { ...EMPTY_DRAFT, code: "legacy", displayName: "Legacy", campaignPrecedence: "" },
      undefined,
    );

    expect(request.campaignPrecedence).toBeUndefined();
  });

  it("never sends a duration for a campaign, even if one was left over from Standard", () => {
    const draft = withCampaignKind(
      { ...EMPTY_DRAFT, code: "annual15", displayName: "Year one", durationPeriods: "3" },
      "FirstAnnualPeriod",
    );

    const request = toCreateDiscountRequest(draft, undefined);

    expect(request.durationPeriods).toBeUndefined();
    expect(request.campaignKind).toBe("FirstAnnualPeriod");
  });

  it("converts a Standard discount start and expiry from local inputs to UTC instants", () => {
    const startsAtUtc = "2026-10-01T09:30";
    const expiresAtUtc = "2026-10-31T18:00";
    const draft: CampaignDraft = {
      ...EMPTY_DRAFT,
      code: "october",
      displayName: "October offer",
      startsAtUtc,
      expiresAtUtc,
    };

    const request = toCreateDiscountRequest(draft, undefined);

    expect(request.startsAtUtc).toBe(new Date(startsAtUtc).toISOString());
    expect(request.expiresAtUtc).toBe(new Date(expiresAtUtc).toISOString());
  });

  it("converts a fixed amount to minor units in the request currency", () => {
    const draft: CampaignDraft = {
      ...EMPTY_DRAFT,
      code: "flat10",
      displayName: "Ten off",
      discountKind: "fixed",
      amount: "10",
      currencyCode: "USD",
    };

    const request = toCreateDiscountRequest(draft, undefined);

    expect(request.amountMinor).toBe(1_000);
    expect(request.percentBasisPoints).toBeUndefined();
  });

  it("sends the entitlement override only for a free-opening-period campaign", () => {
    const draft = withCampaignKind(
      {
        ...EMPTY_DRAFT,
        code: "free1",
        displayName: "Free month",
        entitlementKey: "seats",
        entitlementLimit: "1",
      },
      "FreeOpeningCalendarPeriod",
    );

    const request = toCreateDiscountRequest(draft, undefined);

    expect(request.entitlementOverrideKey).toBe("seats");
    expect(request.entitlementOverrideLimit).toBe(1);
  });
});
