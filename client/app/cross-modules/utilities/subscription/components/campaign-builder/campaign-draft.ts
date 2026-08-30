import { describeDiscountAmountProblem } from "../../utilities/discount-amount";
import { toMajorUnits, toMinorUnits } from "../../utilities/subscription-format";
import type {
  CampaignKind,
  CampaignPrecedence,
  CreateSubscriptionDiscountRequest,
  PlanPrice,
  SubscriptionPlan,
  SubscriptionDiscount,
  UpdateSubscriptionDiscountRequest,
} from "../../models/subscription-plan.model";

/**
 * Everything the four-step wizard collects, in the units a human types them in — cents become
 * minor units, dates become ISO strings, only at {@link toCreateDiscountRequest}.
 *
 * One flat object rather than one per step: a later step's validity can depend on an earlier
 * step's answer (the campaign kind picked in Identity decides most of what Benefit and
 * Eligibility even show), so splitting the state the same way the steps are split would mean
 * threading cross-step reads back through step boundaries for no benefit.
 */
export interface CampaignDraft {
  code: string;
  displayName: string;
  campaignKind: CampaignKind;
  discountKind: "percent" | "fixed";
  percent: string;
  amount: string;
  currencyCode: string;
  campaignPrecedence: CampaignPrecedence;
  /** Standard only — a campaign's own duration is forced server-side and never authored here. */
  durationPeriods: string;
  expiresAtUtc: string;
  planCodes: string[];
  priceIds: string[];
  /** Local calendar dates, "yyyy-MM-dd" — campaign kinds only. */
  validFromDate: string;
  validThroughDate: string;
  timeZoneId: string;
  oneUsePerOrganization: boolean;
  requiresPaymentMethodUpfront: boolean;
  /** FreeOpeningCalendarPeriod only — refused by the server for any other kind. */
  entitlementKey: string;
  entitlementLimit: string;
}

export const EMPTY_DRAFT: CampaignDraft = {
  code: "",
  displayName: "",
  campaignKind: "Standard",
  discountKind: "percent",
  percent: "10",
  amount: "",
  currencyCode: "USD",
  campaignPrecedence: "BestDiscount",
  durationPeriods: "",
  expiresAtUtc: "",
  planCodes: [],
  priceIds: [],
  validFromDate: "",
  validThroughDate: "",
  timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC",
  oneUsePerOrganization: false,
  requiresPaymentMethodUpfront: false,
  entitlementKey: "",
  entitlementLimit: "",
};

export const discountToDraft = (discount: SubscriptionDiscount): CampaignDraft => ({
  code: discount.code,
  displayName: discount.displayName,
  campaignKind: discount.campaignKind,
  discountKind: discount.kind === "Percent" ? "percent" : "fixed",
  percent: discount.percentBasisPoints == null ? "" : String(discount.percentBasisPoints / 100),
  amount: discount.amountMinor == null
    ? ""
    : String(toMajorUnits(discount.amountMinor, discount.currencyCode ?? "USD")),
  currencyCode: discount.currencyCode ?? "USD",
  campaignPrecedence: discount.campaignPrecedence,
  durationPeriods: discount.durationPeriods == null ? "" : String(discount.durationPeriods),
  expiresAtUtc: discount.expiresAtUtc?.slice(0, 16) ?? "",
  planCodes: [...discount.applicablePlanCodes],
  priceIds: [...(discount.applicablePriceIds ?? [])],
  validFromDate: discount.validFromDate ?? "",
  validThroughDate: discount.validThroughDate ?? "",
  timeZoneId: discount.timeZoneId ?? "UTC",
  oneUsePerOrganization: discount.oneUsePerOrganization,
  requiresPaymentMethodUpfront: discount.requiresPaymentMethodUpfront,
  entitlementKey: discount.entitlementOverrideKey ?? "",
  entitlementLimit: discount.entitlementOverrideLimit == null
    ? ""
    : String(discount.entitlementOverrideLimit),
});

/**
 * Applies each campaign kind's own locked-in rules the moment it is picked, the same way the
 * server would refuse anything that disagreed with them -- so a switch to
 * FreeOpeningCalendarPeriod cannot leave behind a 25% rate or a cleared "requires payment method"
 * toggle that Benefit and Eligibility would otherwise have to notice and correct on their own.
 */
export const withCampaignKind = (draft: CampaignDraft, campaignKind: CampaignKind): CampaignDraft => {
  if (campaignKind === "FreeOpeningCalendarPeriod") {
    return {
      ...draft,
      campaignKind,
      discountKind: "percent",
      percent: "100",
      campaignPrecedence: "ReplaceBuiltIn",
      oneUsePerOrganization: true,
      requiresPaymentMethodUpfront: true,
    };
  }

  if (campaignKind === "FirstAnnualPeriod") {
    // Never enforced for this kind -- see CampaignDiscountRequestValidator on the server.
    return {
      ...draft,
      campaignKind,
      campaignPrecedence: "ReplaceBuiltIn",
      entitlementKey: "",
      entitlementLimit: "",
    };
  }

  return { ...draft, campaignKind };
};

/** Only a price this campaign kind could ever be redeemed against is worth offering. */
export const eligiblePrices = (
  campaignKind: CampaignKind,
  plans: SubscriptionPlan[],
): { plan: SubscriptionPlan; price: PlanPrice }[] => {
  const all = plans.flatMap((plan) => plan.prices.map((price) => ({ plan, price })));

  if (campaignKind === "Standard") {
    return all;
  }

  if (campaignKind === "FreeOpeningCalendarPeriod") {
    return all.filter(
      ({ price }) => price.interval === "Month" && price.billingAlignment === "CalendarMonth",
    );
  }

  // FirstAnnualPeriod: a calendar-aligned year with a stub base actually configured -- otherwise
  // it falls back to an anniversary year at runtime, which this campaign kind cannot price
  // correctly. Mirrors DiscountCatalogueService.CheckCadence exactly.
  return all.filter(
    ({ price }) =>
      price.interval === "Year" &&
      price.billingAlignment === "CalendarMonth" &&
      (price.calendarStubBaseUnitAmountMinor ?? 0) > 0,
  );
};

export type StepId = 1 | 2 | 3 | 4;

/** Every reason this step cannot yet be left. Empty means it can. */
export const stepProblems = (
  step: StepId,
  draft: CampaignDraft,
  plans: SubscriptionPlan[],
): string[] => {
  if (step === 1) {
    const problems: string[] = [];

    if (!draft.code.trim()) {
      problems.push("Enter a code.");
    } else if (!/^[a-z0-9_-]+$/.test(draft.code.trim())) {
      problems.push("Code may only contain lowercase letters, digits, hyphens and underscores.");
    }

    if (!draft.displayName.trim()) {
      problems.push("Enter a display name.");
    }

    return problems;
  }

  if (step === 2) {
    const problems: string[] = [];

    if (draft.discountKind === "percent") {
      const percent = Number(draft.percent);
      if (draft.percent.trim() === "" || !Number.isFinite(percent) || percent <= 0 || percent > 100) {
        problems.push("Percent off must be between 0 and 100.");
      }
      if (draft.campaignKind === "FreeOpeningCalendarPeriod" && percent !== 100) {
        problems.push("A free-opening-period campaign must be a full 100% reduction.");
      }
    } else {
      if (draft.campaignKind === "FreeOpeningCalendarPeriod") {
        problems.push("A free-opening-period campaign cannot be a fixed amount — it must be 100% off.");
      }
      const amountProblem = describeDiscountAmountProblem(draft.amount, draft.currencyCode);
      if (amountProblem) problems.push(amountProblem);
    }

    if (draft.campaignKind === "Standard" && draft.durationPeriods.trim() !== "") {
      const duration = Number(draft.durationPeriods);
      if (!Number.isInteger(duration) || duration <= 0) {
        problems.push("Duration in billing periods must be a whole number greater than zero.");
      }
    }

    return problems;
  }

  if (step === 3) {
    const problems: string[] = [];
    const isCampaign = draft.campaignKind !== "Standard";

    if (isCampaign && draft.priceIds.length === 0) {
      problems.push(
        "This campaign kind needs at least one price named — it prices a specific price's " +
          "opening period or first annual period, not a plan in general.",
      );
    }

    if (isCampaign) {
      if (!draft.validFromDate) problems.push("A campaign needs a start date.");
      if (!draft.validThroughDate && draft.campaignKind !== "FreeOpeningCalendarPeriod") {
        problems.push("A campaign needs an end date.");
      }
      if (!draft.timeZoneId.trim()) problems.push("A campaign needs a time zone its dates are read in.");
      if (
        draft.validFromDate &&
        draft.validThroughDate &&
        draft.validThroughDate < draft.validFromDate
      ) {
        problems.push("The campaign cannot end before it starts.");
      }
    }

    if (draft.campaignKind === "FreeOpeningCalendarPeriod") {
      if (!draft.entitlementKey.trim()) {
        problems.push("A free-opening-period campaign must name the entitlement it temporarily caps.");
      }
      const limit = Number(draft.entitlementLimit);
      if (draft.entitlementLimit.trim() === "" || !Number.isFinite(limit) || limit <= 0) {
        problems.push("The temporary entitlement limit must be a positive number.");
      }
      const plan = plans.find((candidate) => draft.planCodes.includes(candidate.code)) ??
        plans.find((candidate) =>
          draft.priceIds.some((priceId) =>
            candidate.prices.some((price) => price.priceId === priceId)));
      const entitlement = plan?.entitlements.find(
        (candidate) => candidate.key === draft.entitlementKey.trim(),
      );
      if (plan && draft.entitlementKey.trim() && !entitlement) {
        problems.push(`The plan '${plan.code}' has no entitlement '${draft.entitlementKey.trim()}'.`);
      }
    }

    return problems;
  }

  return [];
};

export const canSubmit = (draft: CampaignDraft, plans: SubscriptionPlan[]): boolean =>
  ([1, 2, 3] as const).every((step) => stepProblems(step, draft, plans).length === 0);

/** The exact request `POST /api/subscription-discounts` expects — units converted here, once. */
export const toCreateDiscountRequest = (
  draft: CampaignDraft,
  organizationId: string | undefined,
): CreateSubscriptionDiscountRequest => {
  const isCampaign = draft.campaignKind !== "Standard";

  return {
    organizationId,
    code: draft.code.trim(),
    displayName: draft.displayName.trim(),
    kind: draft.discountKind === "percent" ? 0 : 1,
    percentBasisPoints:
      draft.discountKind === "percent" ? Math.round(Number(draft.percent) * 100) : undefined,
    amountMinor:
      draft.discountKind === "fixed" ? toMinorUnits(Number(draft.amount), draft.currencyCode) : undefined,
    currencyCode: draft.discountKind === "fixed" ? draft.currencyCode : undefined,
    // A campaign's own duration is forced server-side regardless of what is sent, so nothing here
    // ever claims to set one — sending it would suggest an author-controlled figure that a
    // campaign kind silently overrides.
    durationPeriods:
      !isCampaign && draft.durationPeriods.trim() !== "" ? Number(draft.durationPeriods) : undefined,
    expiresAtUtc: !isCampaign && draft.expiresAtUtc ? new Date(draft.expiresAtUtc).toISOString() : undefined,
    applicablePlanCodes: draft.planCodes,
    applicablePriceIds: draft.priceIds,
    campaignKind: isCampaign ? draft.campaignKind : undefined,
    campaignPrecedence: isCampaign ? draft.campaignPrecedence : undefined,
    validFromDate: isCampaign ? draft.validFromDate : undefined,
    validThroughDate: isCampaign && draft.validThroughDate ? draft.validThroughDate : undefined,
    timeZoneId: isCampaign ? draft.timeZoneId : undefined,
    oneUsePerOrganization: isCampaign ? draft.oneUsePerOrganization : undefined,
    requiresPaymentMethodUpfront: isCampaign ? draft.requiresPaymentMethodUpfront : undefined,
    entitlementOverrideKey:
      draft.campaignKind === "FreeOpeningCalendarPeriod" ? draft.entitlementKey.trim() : undefined,
    entitlementOverrideLimit:
      draft.campaignKind === "FreeOpeningCalendarPeriod" ? Number(draft.entitlementLimit) : undefined,
  };
};

export const toUpdateDiscountRequest = (
  draft: CampaignDraft,
  expectedVersion: number,
): UpdateSubscriptionDiscountRequest => {
  const { organizationId: _organizationId, code: _code, ...request } =
    toCreateDiscountRequest(draft, undefined);
  return { ...request, expectedVersion };
};
