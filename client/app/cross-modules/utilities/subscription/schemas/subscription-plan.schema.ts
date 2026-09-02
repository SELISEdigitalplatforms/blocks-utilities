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
import {
  isWithinMagnitude,
  isWithinScale,
  METER_QUANTITY_MAX_SCALE,
} from "../utilities/meter-quantity";
import {
  isRepresentableInMinorUnits,
  minorUnitExponent,
} from "../utilities/subscription-format";

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
  upToQuantity: z.coerce.number().positive().optional(),
  /**
   * What one unit past the allowance costs, in the currency's own units.
   *
   * Minor units before this: the field asked for 5 and meant five centimes, so an author pricing
   * overage at five francs typed 5 and undercharged by a hundredfold. The API still carries
   * `unitAmountMinor`; the conversion happens on submit, where the currency is known.
   *
   * Zero is allowed and means exactly what it says — overage recorded, permitted, billed nothing.
   * Whether it was intended is the rate table's business, not this field's.
   */
  unitAmount: z.coerce.number().min(0, "A tier price cannot be negative."),
});

const meterRateTableSchema = z
  .object({
    currencyCode: z.string().trim().length(3, "Use a three-letter currency code.").toUpperCase(),
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

    // Checked here rather than on the field, because how many decimals are allowed depends on the
    // currency chosen above it. Without this the amount is rounded on the way to minor units and
    // the author is never told: 0.005 CHF is charged as 0.01, and 100.5 JPY as 101.
    const exponent = minorUnitExponent(table.currencyCode);

    table.tiers.forEach((tier, index) => {
      if (isRepresentableInMinorUnits(tier.unitAmount, table.currencyCode)) {
        return;
      }

      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["tiers", index, "unitAmount"],
        message:
          exponent === 0
            ? `${table.currencyCode} has no decimal places — enter a whole amount.`
            : `${table.currencyCode} allows at most ${exponent} decimal places.`,
      });
    });
  });

const meterSchema = z.object({
  meterKey: key("meter key"),
  displayName: z.string().trim().min(1, "Enter a display name.").max(200),
  unitLabel: key("unit label"),
  aggregation: z.coerce.number().int().min(0).max(2),
  resetPolicy: z.coerce.number().int().min(0).max(2).default(0),
  /**
   * Decimal places this meter accepts. Zero is whole units only, and is the default so that a
   * meter nobody opts in stays exactly as strict as it was before fractions existed.
   */
  quantityScale: z.coerce
    .number()
    .int()
    .min(0)
    .max(METER_QUANTITY_MAX_SCALE)
    .default(0),
  includedQuantity: z.coerce.number().min(0),
  carryForwardCap: z.coerce.number().positive().optional(),
  overageAllowed: z.boolean(),
  thresholdPercents: z.array(z.number().int().min(1).max(100)),
  rateTables: z.array(meterRateTableSchema),
});

/**
 * One volume band, as the builder edits it.
 *
 * A percentage rather than the basis points the API carries: nobody authoring a price list thinks
 * in ten-thousandths, and 5 typed where 500 was meant is a 0.05% discount that looks plausible in
 * a table and is wrong by two orders of magnitude.
 */
const quantityDiscountTierSchema = z.object({
  minimumQuantity: z.coerce.number().int().positive(),
  maximumQuantity: z.coerce.number().int().positive().optional(),
  discountPercent: z.coerce
    .number()
    .min(0, "A discount cannot be negative.")
    .max(100, "A discount cannot exceed 100%."),
});

const quantityItemSchema = z
  .object({
    itemKey: key("item key"),
    unitLabel: key("unit label"),
    minQuantity: z.coerce.number().int().min(0),
    maxQuantity: z.coerce.number().int().positive().optional(),
    defaultQuantity: z.coerce.number().int().min(0),
    quantityDiscountTiers: z.array(quantityDiscountTierSchema).default([]),
  })
  .superRefine((item, context) => {
    if (item.maxQuantity !== undefined && item.maxQuantity < item.minQuantity) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["maxQuantity"],
        message: "The maximum cannot be below the minimum.",
      });
    }

    checkQuantityDiscountTiers(item, context);
  });

/**
 * The band rules, checked here rather than left to the server.
 *
 * The server refuses a gap, an overlap or a second open end with one error code for the whole
 * plan, which tells an author that something is wrong and nothing about where. These are the same
 * rules said per row, at the row that breaks them.
 */
const checkQuantityDiscountTiers = (
  item: {
    minQuantity: number;
    maxQuantity?: number;
    quantityDiscountTiers: { minimumQuantity: number; maximumQuantity?: number }[];
  },
  context: z.RefinementCtx,
) => {
  const tiers = item.quantityDiscountTiers;

  if (tiers.length === 0) {
    return;
  }

  const at = (index: number, field: string, message: string) =>
    context.addIssue({
      code: z.ZodIssueCode.custom,
      path: ["quantityDiscountTiers", index, field],
      message,
    });

  // One band is a single discount on everything, which the unit price already expresses.
  if (tiers.length < 2) {
    at(0, "maximumQuantity", "Add a second band, or turn volume discounts off.");
  }

  if (tiers[0].minimumQuantity !== item.minQuantity) {
    at(0, "minimumQuantity", `The first band must start at ${item.minQuantity}.`);
  }

  tiers.forEach((tier, index) => {
    const isLast = index === tiers.length - 1;

    if (tier.maximumQuantity !== undefined && tier.maximumQuantity < tier.minimumQuantity) {
      at(index, "maximumQuantity", "The band cannot end below where it starts.");

      return;
    }

    if (!isLast && tier.maximumQuantity === undefined) {
      at(index, "maximumQuantity", "Only the final band can be left open.");

      return;
    }

    // Contiguity in both directions at once: the next band starting anywhere but one past this
    // one is either a gap nothing prices or an overlap two bands claim.
    const next = tiers[index + 1];

    if (next && tier.maximumQuantity !== undefined) {
      const expected = tier.maximumQuantity + 1;

      if (next.minimumQuantity !== expected) {
        at(index + 1, "minimumQuantity", `The next band must begin at ${expected}.`);
      }
    }

    if (!isLast) {
      return;
    }

    if (item.maxQuantity === undefined && tier.maximumQuantity !== undefined) {
      at(
        index,
        "maximumQuantity",
        "This item has no maximum, so the final band must be left open.",
      );

      return;
    }

    if (item.maxQuantity !== undefined && tier.maximumQuantity !== item.maxQuantity) {
      at(index, "maximumQuantity", `The final band must cover quantities up to ${item.maxQuantity}.`);
    }
  });
};

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
  // Granularity is checked against the meter this grant replaces, at plan level, because that is
  // the only place the meter's own scale is in scope.
  includedQuantity: z.coerce.number().min(0),
});

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
        .regex(/^[a-z0-9_-]+$/, "Use only lowercase letters, digits, hyphens and underscores."),
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
      // The console always authors through these two rather than the legacy trialDays — see the
      // cross-field checks below for what each duration kind requires.
      trialDurationKind: z.enum(["Days", "EndOfCalendarMonth", "AnniversaryMonths"]).optional(),
      trialDurationCount: z.preprocess(
        (value) => (value === "" ? undefined : value),
        z.coerce.number().int().optional(),
      ),
      trialRequiresPaymentMethod: z.boolean(),
      requirePaymentMethodUpfront: z.boolean(),
      // Always in the form, whether or not any item has bands: a plan edited while the field
      // was absent came back from the server reset to BestDiscount, which changes what every
      // subscriber on it is charged.
      quantityDiscountCombinationPolicy: z.coerce.number().int().min(0).max(2).default(0),
      usageInterval: z.coerce.number().int().min(0).max(3),
      usageIntervalCount: z.coerce.number().int().min(1).max(100),
      familyCode: z.string().trim().max(SUBSCRIPTION_KEY_MAX_LENGTH).optional().or(z.literal("")),
      familyRank: z.preprocess(
        (value) => (value === "" ? undefined : value),
        z.coerce.number().int().min(0).optional(),
      ),
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
          message: 'Features must be a valid JSON object, e.g. {"betaAccess": true}.',
        });
      }

      // Mirrors the server's PlanDefinitionRequestValidator rules for trial duration exactly —
      // the two must agree, or an edit that passes here could still be rejected on save.
      if (plan.trialDurationKind === "Days" && plan.trialDurationCount === undefined) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["trialDurationCount"],
          message: "Enter how many days the trial lasts.",
        });
      } else if (
        plan.trialDurationKind === "Days" &&
        (plan.trialDurationCount! < 1 || plan.trialDurationCount! > 365)
      ) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["trialDurationCount"],
          message: "A day-based trial must be between 1 and 365 days.",
        });
      }

      if (plan.trialDurationKind === "AnniversaryMonths" && plan.trialDurationCount === undefined) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["trialDurationCount"],
          message: "Enter how many months the trial lasts.",
        });
      } else if (
        plan.trialDurationKind === "AnniversaryMonths" &&
        (plan.trialDurationCount! < 1 || plan.trialDurationCount! > 12)
      ) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["trialDurationCount"],
          message: "An anniversary-month trial must be between 1 and 12 months.",
        });
      }

      if (plan.trialDurationKind === "EndOfCalendarMonth" && plan.trialDurationCount !== undefined) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["trialDurationCount"],
          message: "An end-of-calendar-month trial has no count to set.",
        });
      }

      if (Boolean(plan.familyCode) !== (plan.familyRank !== undefined)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          path: [plan.familyCode ? "familyRank" : "familyCode"],
          message: "Family code and rank must be supplied together.",
        });
      }

      const meterKeys = new Set(plan.meters.map((meter) => meter.meterKey));
      const lifetimeMeterKeys = new Set(
        plan.meters.filter((meter) => meter.resetPolicy === 1).map((meter) => meter.meterKey),
      );

      plan.meters.forEach((meter, index) => {
        // A cap is what stops a dormant subscription banking allowance forever, so it is
        // required rather than optional — and meaningless on a policy that does not roll.
        if (meter.resetPolicy === 2 && !meter.carryForwardCap) {
          context.addIssue({
            code: z.ZodIssueCode.custom,
            path: ["meters", index, "carryForwardCap"],
            message: "Set the most one period may carry in.",
          });
        }

        if (meter.resetPolicy !== 2 && meter.carryForwardCap !== undefined) {
          context.addIssue({
            code: z.ZodIssueCode.custom,
            path: ["meters", index, "carryForwardCap"],
            message: "Only a carry-forward meter has a carry-forward cap.",
          });
        }

        // Every quantity the meter carries has to be one the meter can hold. Reported against the
        // field that is wrong rather than against the scale, so the author is taken to the number
        // they have to change.
        const holds = (value: number): boolean =>
          isWithinScale(value, meter.quantityScale) && isWithinMagnitude(value);

        const tooFine = meter.quantityScale === 0
          ? "This meter takes whole numbers only — set its decimal places to allow a fraction."
          : `This meter allows at most ${meter.quantityScale} decimal places.`;

        if (!holds(meter.includedQuantity)) {
          context.addIssue({
            code: z.ZodIssueCode.custom,
            path: ["meters", index, "includedQuantity"],
            message: tooFine,
          });
        }

        if (meter.carryForwardCap !== undefined && !holds(meter.carryForwardCap)) {
          context.addIssue({
            code: z.ZodIssueCode.custom,
            path: ["meters", index, "carryForwardCap"],
            message: tooFine,
          });
        }

        meter.rateTables.forEach((table, tableIndex) => {
          table.tiers.forEach((tier, tierIndex) => {
            if (tier.upToQuantity !== undefined && !holds(tier.upToQuantity)) {
              context.addIssue({
                code: z.ZodIssueCode.custom,
                path: ["meters", index, "rateTables", tableIndex, "tiers", tierIndex, "upToQuantity"],
                message: tooFine,
              });
            }
          });
        });

        if (meter.resetPolicy === 1 && (meter.overageAllowed || meter.rateTables.length > 0)) {
          context.addIssue({
            code: z.ZodIssueCode.custom,
            path: ["meters", index, "resetPolicy"],
            message:
              "Lifetime capacity must stop at its allowance; it cannot use monthly overage billing.",
          });
        }
      });

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
        } else if (lifetimeMeterKeys.has(grant.meterKey)) {
          context.addIssue({
            code: z.ZodIssueCode.custom,
            path: ["trialGrants", index, "meterKey"],
            message: "A lifetime capacity cannot have a separate trial allowance.",
          });
        } else {
          // A grant replaces its meter's allowance, so it has to be a quantity that meter can hold.
          const meter = plan.meters.find((candidate) => candidate.meterKey === grant.meterKey);

          if (
            meter &&
            (!isWithinScale(grant.includedQuantity, meter.quantityScale) ||
              !isWithinMagnitude(grant.includedQuantity))
          ) {
            context.addIssue({
              code: z.ZodIssueCode.custom,
              path: ["trialGrants", index, "includedQuantity"],
              message:
                meter.quantityScale === 0
                  ? "This meter takes whole numbers only."
                  : `This meter allows at most ${meter.quantityScale} decimal places.`,
            });
          }
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
  trialDurationKind: undefined,
  trialDurationCount: undefined,
  trialRequiresPaymentMethod: true,
  // False, which is what every plan authored before this existed meant: nothing due today, so
  // nothing to collect.
  requirePaymentMethodUpfront: false,
  quantityDiscountCombinationPolicy: 0,
  usageInterval: 2,
  usageIntervalCount: 1,
  familyCode: "",
  familyRank: undefined,
  quantityItems: [],
  meters: [],
  entitlements: [],
  trialGrants: [],
  // One empty row, not none: a plan needs a price, and an admin who never notices the section is
  // the one who ends up with a plan nobody can subscribe to.
  prices: [defaultSubscriptionPriceFormValues],
};
