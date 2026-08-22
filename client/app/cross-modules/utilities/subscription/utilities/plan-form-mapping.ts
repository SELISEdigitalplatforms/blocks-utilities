import { TENANT_WIDE_ORGANIZATION } from "../constants/subscription.constants";
import {
  BILLING_INTERVAL,
  ENTITLEMENT_LIMIT_KIND,
  METER_AGGREGATION,
  METER_RESET_POLICY,
  type CreateSubscriptionPlanRequest,
  type SubscriptionPlan,
  type UpdateSubscriptionPlanRequest,
} from "../models/subscription-plan.model";
import type { CreateSubscriptionPlanFormValues } from "../schemas/subscription-plan.schema";
import { defaultSubscriptionPlanFormValues } from "../schemas/subscription-plan.schema";

/**
 * What a plan is made of, in the shape both creating and editing send. Everything the two have in
 * common lives here so an edit cannot quietly send a different body than a create.
 */
const toPlanDefinition = (values: CreateSubscriptionPlanFormValues) => ({
  displayName: values.displayName.trim(),
  description: values.description?.trim() || undefined,
  featuresJson: values.featuresJson?.trim() || undefined,
  trialDays: values.trialDays,
  trialRequiresPaymentMethod: values.trialRequiresPaymentMethod,
  usageInterval: values.usageInterval,
  usageIntervalCount: values.usageIntervalCount,
  familyCode: values.familyCode?.trim() || undefined,
  familyRank: values.familyRank,
  quantityItems: values.quantityItems.map((item) => ({
    itemKey: item.itemKey.trim(),
    unitLabel: item.unitLabel.trim(),
    minQuantity: item.minQuantity,
    maxQuantity: item.maxQuantity,
    defaultQuantity: item.defaultQuantity,
    // Omitted rather than sent empty: an empty array and an absent field mean the same thing to
    // the API, and sending one keeps a plan with no bands looking like a plan whose bands were
    // deleted.
    quantityDiscountTiers: item.quantityDiscountTiers?.length
      ? item.quantityDiscountTiers.map((tier) => ({
          minimumQuantity: tier.minimumQuantity,
          maximumQuantity: tier.maximumQuantity,
          // Percent in the form, basis points on the wire. Rounded because 5.005 is not a
          // discount anyone authored, and truncating it would quietly favour the merchant.
          discountBasisPoints: Math.round(tier.discountPercent * 100),
        }))
      : undefined,
  })),
  meters: values.meters.map((meter) => ({
    meterKey: meter.meterKey.trim(),
    displayName: meter.displayName.trim(),
    unitLabel: meter.unitLabel.trim(),
    aggregation: meter.aggregation,
    resetPolicy: meter.resetPolicy,
    carryForwardCap: meter.carryForwardCap,
    includedQuantity: meter.includedQuantity,
    overageAllowed: meter.overageAllowed,
    thresholdPercents: meter.thresholdPercents,
    rateTables: meter.rateTables.map((table) => ({
      currencyCode: table.currencyCode,
      tiers: table.tiers,
    })),
  })),
  entitlements: values.entitlements.map((entitlement) => ({
    key: entitlement.key.trim(),
    limitKind: entitlement.limitKind,
    limit: entitlement.limit,
    meterKey: entitlement.meterKey?.trim() || undefined,
    unitLabel: entitlement.unitLabel?.trim() || undefined,
  })),
  trialGrants: values.trialGrants.map((grant) => ({
    meterKey: grant.meterKey.trim(),
    includedQuantity: grant.includedQuantity,
  })),
});

export const toCreatePlanRequest = (
  values: CreateSubscriptionPlanFormValues,
): CreateSubscriptionPlanRequest => ({
  code: values.code.trim(),
  organizationId:
    values.organizationId === TENANT_WIDE_ORGANIZATION ? undefined : values.organizationId,
  ...toPlanDefinition(values),
});

/**
 * @param organizationId
 * The plan's own organization, which the server needs to find it — it never moves the scope.
 */
export const toUpdatePlanRequest = (
  values: CreateSubscriptionPlanFormValues,
  organizationId: string | null,
): UpdateSubscriptionPlanRequest => ({
  organizationId: organizationId ?? undefined,
  ...toPlanDefinition(values),
});

/** Response enums arrive as names; the form and the request body both want the number. */
const aggregationValue = (name: string): number =>
  METER_AGGREGATION[name as keyof typeof METER_AGGREGATION] ?? METER_AGGREGATION.Sum;

const resetPolicyValue = (name?: string): number =>
  METER_RESET_POLICY[name as keyof typeof METER_RESET_POLICY] ?? METER_RESET_POLICY.Periodic;

const limitKindValue = (name: string): number =>
  ENTITLEMENT_LIMIT_KIND[name as keyof typeof ENTITLEMENT_LIMIT_KIND] ??
  ENTITLEMENT_LIMIT_KIND.Boolean;

/**
 * A stored plan as a builder draft.
 *
 * Prices start empty rather than loaded: the update endpoint does not touch prices, so the ones
 * the plan already has are left where they are and anything listed here is a new price to create.
 * Showing them as editable rows would promise an edit that cannot happen.
 */
export const planToFormValues = (plan: SubscriptionPlan): CreateSubscriptionPlanFormValues => ({
  ...defaultSubscriptionPlanFormValues,
  code: plan.code,
  displayName: plan.displayName,
  description: plan.description ?? "",
  featuresJson: plan.featuresJson ?? "",
  organizationId: plan.organizationId ?? TENANT_WIDE_ORGANIZATION,
  trialDays: plan.trialDays ?? undefined,
  trialRequiresPaymentMethod: plan.trialRequiresPaymentMethod,
  usageInterval: plan.usageInterval
    ? (BILLING_INTERVAL[plan.usageInterval] ?? BILLING_INTERVAL.Month)
    : BILLING_INTERVAL.Month,
  usageIntervalCount: plan.usageIntervalCount ?? 1,
  familyCode: plan.familyCode ?? "",
  familyRank: plan.familyRank ?? undefined,
  quantityItems: plan.quantityItems.map((item) => ({
    itemKey: item.itemKey,
    unitLabel: item.unitLabel,
    minQuantity: item.minQuantity,
    maxQuantity: item.maxQuantity ?? undefined,
    defaultQuantity: item.defaultQuantity,
    // Plans stored before bands existed have no field at all, and reopen with the control off
    // rather than with a row nobody authored.
    quantityDiscountTiers: (item.quantityDiscountTiers ?? []).map((tier) => ({
      minimumQuantity: tier.minimumQuantity,
      maximumQuantity: tier.maximumQuantity ?? undefined,
      discountPercent: tier.discountBasisPoints / 100,
    })),
  })),
  meters: plan.meters.map((meter) => ({
    meterKey: meter.meterKey,
    displayName: meter.displayName,
    unitLabel: meter.unitLabel,
    aggregation: aggregationValue(meter.aggregation),
    resetPolicy: resetPolicyValue(meter.resetPolicy),
    carryForwardCap: meter.carryForwardCap ?? undefined,
    includedQuantity: meter.includedQuantity,
    overageAllowed: meter.overageAllowed,
    thresholdPercents: meter.thresholdPercents ?? [],
    rateTables: (meter.rateTables ?? []).map((table) => ({
      currencyCode: table.currencyCode,
      tiers: table.tiers.map((tier) => ({
        upToQuantity: tier.upToQuantity ?? undefined,
        unitAmountMinor: tier.unitAmountMinor,
      })),
    })),
  })),
  entitlements: plan.entitlements.map((entitlement) => ({
    key: entitlement.key,
    limitKind: limitKindValue(entitlement.limitKind),
    limit: entitlement.limit ?? undefined,
    meterKey: entitlement.meterKey ?? undefined,
    unitLabel: entitlement.unitLabel ?? undefined,
  })),
  trialGrants: (plan.trialGrants ?? []).map((grant) => ({
    meterKey: grant.meterKey,
    includedQuantity: grant.includedQuantity,
  })),
  prices: [],
});
