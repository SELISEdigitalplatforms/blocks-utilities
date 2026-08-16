import { z } from "zod";
import {
  SUBSCRIPTION_DISPLAY_NAME_MAX_LENGTH,
  SUBSCRIPTION_FEATURES_JSON_MAX_LENGTH,
  SUBSCRIPTION_KEY_MAX_LENGTH,
  SUBSCRIPTION_PLAN_CODE_MAX_LENGTH,
  TENANT_WIDE_ORGANIZATION,
} from "../constants/subscription.constants";
import {
  defaultSubscriptionPriceFormValues,
  FLAT_FEE,
  subscriptionPriceFieldsSchema,
} from "./subscription-price.schema";

// "unit" starts with a consonant sound ("you-nit") despite its spelling, so a plain vowel-letter
// check would wrongly produce "an unit label" — the one exception among today's labels.
const CONSONANT_SOUNDING = new Set(["unit label"]);

const article = (label: string) =>
  !CONSONANT_SOUNDING.has(label) && /^[aeiou]/i.test(label) ? "an" : "a";

const key = (label: string) =>
  z
    .string()
    .trim()
    .min(1, `Enter ${article(label)} ${label}.`)
    .max(SUBSCRIPTION_KEY_MAX_LENGTH);

const isJsonObject = (value: string): boolean => {
  try {
    return typeof JSON.parse(value) === "object" && JSON.parse(value) !== null;
  } catch {
    return false;
  }
};

const meterTierSchema = z.object({
  upToQuantity: z.coerce.number().int().positive().optional(),
  unitAmountMinor: z.coerce.number().int().min(0),
});

const meterRateTableSchema = z
  .object({
    currencyCode: z
      .string()
      .trim()
      .length(3, "Use a three-letter currency code.")
      .toUpperCase(),
    tiers: z.array(meterTierSchema).min(1, "Add at least one tier."),
  })
  .superRefine((table, context) => {
    // Mirrors the server's rule. Bands must ascend and only the last may be open-ended,
    // otherwise a quantity falls into two of them and the bill depends on which is read first.
    const bounded = table.tiers.slice(0, -1);

    if (bounded.some((tier) => tier.upToQuantity === undefined)) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["tiers"],
        message: "Only the last band may be unbounded.",
      });
    }

    const bounds = table.tiers
      .map((tier) => tier.upToQuantity)
      .filter((bound): bound is number => bound !== undefined);

    if (bounds.some((bound, index) => index > 0 && bound <= bounds[index - 1])) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["tiers"],
        message: "Each band must end above the one before it.",
      });
    }
  });

const meterSchema = z.object({
  meterKey: key("meter key"),
  displayName: z.string().trim().min(1, "Enter a display name.").max(200),
  unitLabel: key("unit label"),
  aggregation: z.coerce.number().int().min(0).max(2),
  includedQuantity: z.coerce.number().int().min(0),
  overageAllowed: z.boolean(),
  thresholdPercents: z.array(z.number().int().min(1).max(100)),
  rateTables: z.array(meterRateTableSchema),
});

const quantityItemSchema = z
  .object({
    itemKey: key("item key"),
    unitLabel: key("unit label"),
    minQuantity: z.coerce.number().int().min(0),
    maxQuantity: z.coerce.number().int().positive().optional(),
    defaultQuantity: z.coerce.number().int().min(0),
  })
  .refine(
    (item) => item.maxQuantity === undefined || item.maxQuantity >= item.minQuantity,
    { message: "The maximum cannot be below the minimum.", path: ["maxQuantity"] },
  );

const entitlementSchema = z
  .object({
    key: key("key"),
    limitKind: z.coerce.number().int().min(0).max(2),
    limit: z.coerce.number().int().min(0).optional(),
    meterKey: z.string().trim().max(SUBSCRIPTION_KEY_MAX_LENGTH).optional(),
    unitLabel: z.string().trim().max(SUBSCRIPTION_KEY_MAX_LENGTH).optional(),
  })
  .refine(
    (entitlement) =>
      entitlement.limitKind !== 1 ||
      (entitlement.limit !== undefined && Boolean(entitlement.meterKey)),
    {
      message: "A counted entitlement needs both a limit and the meter that draws it down.",
      path: ["limit"],
    },
  );

const trialGrantSchema = z.object({
  meterKey: key("meter"),
  includedQuantity: z.coerce.number().int().min(0),
});

export const PRICING_SHAPE_OPTIONS = ["seats", "usage", "both"] as const;
export type PricingShape = (typeof PRICING_SHAPE_OPTIONS)[number];

/** What identifies a price to the server, so two rows that would collide can be caught here. */
const priceTerms = (price: z.infer<typeof subscriptionPriceFieldsSchema>) =>
  `${price.currencyCode}|${price.interval}|${price.intervalCount}|${price.quantityItemKey}`;

/**
 * @param requirePrice
 * Whether the plan must carry at least one price. True when creating — a plan with no price
 * cannot be checked out, so finishing the builder without one produces something unusable. False
 * when editing, where prices already exist and are added separately.
 */
export const buildSubscriptionPlanSchema = ({ requirePrice }: { requirePrice: boolean }) =>
  z
  .object({
    code: z
      .string()
      .trim()
      .min(1, "Enter a plan code.")
      .max(SUBSCRIPTION_PLAN_CODE_MAX_LENGTH)
      .regex(
        /^[a-z0-9_-]+$/,
        "Use only lowercase letters, digits, hyphens and underscores.",
      ),
    displayName: z
      .string()
      .trim()
      .min(1, "Enter a display name.")
      .max(SUBSCRIPTION_DISPLAY_NAME_MAX_LENGTH),
    description: z.string().trim().max(2000).optional().or(z.literal("")),
    featuresJson: z
      .string()
      .trim()
      .max(SUBSCRIPTION_FEATURES_JSON_MAX_LENGTH)
      .optional()
      .or(z.literal("")),
    organizationId: z.string().min(1, "Choose an organization."),
    trialDays: z.coerce.number().int().min(1).max(365).optional(),
    trialRequiresPaymentMethod: z.boolean(),
    pricingShape: z.enum(PRICING_SHAPE_OPTIONS).optional(),
    quantityItems: z.array(quantityItemSchema),
    meters: z.array(meterSchema),
    entitlements: z.array(entitlementSchema),
    trialGrants: z.array(trialGrantSchema),
    prices: z.array(subscriptionPriceFieldsSchema),
  })
  .superRefine((plan, context) => {
    if (plan.featuresJson && !isJsonObject(plan.featuresJson)) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["featuresJson"],
        message: "Features must be a valid JSON object, e.g. {\"betaAccess\": true}.",
      });
    }

    const meterKeys = new Set(plan.meters.map((meter) => meter.meterKey));

    plan.entitlements.forEach((entitlement, index) => {
      if (entitlement.meterKey && !meterKeys.has(entitlement.meterKey)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["entitlements", index, "meterKey"],
          message: "This meter is not defined on this plan.",
        });
      }
    });

    plan.trialGrants.forEach((grant, index) => {
      if (!meterKeys.has(grant.meterKey)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["trialGrants", index, "meterKey"],
          message: "This meter is not defined on this plan.",
        });
      }
    });

    if (requirePrice && plan.prices.length === 0) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["prices"],
        message: "Add at least one price — nobody can subscribe to a plan that has none.",
      });
    }

    const itemKeys = new Set(plan.quantityItems.map((item) => item.itemKey));
    const seenTerms = new Set<string>();

    plan.prices.forEach((price, index) => {
      if (price.quantityItemKey !== FLAT_FEE && !itemKeys.has(price.quantityItemKey)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["prices", index, "quantityItemKey"],
          message: "This plan does not define that quantity item.",
        });
      }

      // The server rejects a second price with the same terms, and it is rejected after the plan
      // itself has been created — so catching it here is the difference between fixing a field
      // and being left with a half-priced plan.
      const terms = priceTerms(price);

      if (seenTerms.has(terms)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["prices", index, "currencyCode"],
          message: "Another price already charges on exactly these terms.",
        });
      }

      seenTerms.add(terms);
    });
  });

export const createSubscriptionPlanSchema = buildSubscriptionPlanSchema({ requirePrice: true });

export type CreateSubscriptionPlanFormValues = z.infer<typeof createSubscriptionPlanSchema>;

export const defaultSubscriptionPlanFormValues: CreateSubscriptionPlanFormValues = {
  code: "",
  displayName: "",
  description: "",
  featuresJson: "",
  organizationId: TENANT_WIDE_ORGANIZATION,
  trialDays: undefined,
  trialRequiresPaymentMethod: true,
  pricingShape: undefined,
  quantityItems: [],
  meters: [],
  entitlements: [],
  trialGrants: [],
  // One empty row, not none: a plan needs a price, and an admin who never notices the section is
  // the one who ends up with a plan nobody can subscribe to.
  prices: [defaultSubscriptionPriceFormValues],
};
