import { z } from "zod";
import {
  SUBSCRIPTION_DISPLAY_NAME_MAX_LENGTH,
  SUBSCRIPTION_FEATURES_JSON_MAX_LENGTH,
  SUBSCRIPTION_KEY_MAX_LENGTH,
  SUBSCRIPTION_PLAN_CODE_MAX_LENGTH,
  TENANT_WIDE_ORGANIZATION,
} from "../constants/subscription.constants";

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

const meterRateTableSchema = z.object({
  currencyCode: z
    .string()
    .trim()
    .length(3, "Use a three-letter currency code.")
    .toUpperCase(),
  tiers: z.array(meterTierSchema).min(1, "Add at least one tier."),
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

export const createSubscriptionPlanSchema = z
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
  });

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
};
