/**
 * Mirrors Subscription.DomainService's request/response enums. There is no
 * JsonStringEnumConverter registered on the server, so every enum field in a *request* body must
 * be sent as this underlying integer — sending the name instead fails model binding. Response
 * DTOs are the opposite: they arrive stringified, because mapper code on the server calls
 * `.ToString()` by hand.
 */
export const BILLING_INTERVAL = {
  Day: 0,
  Week: 1,
  Month: 2,
  Year: 3,
} as const;

export const METER_AGGREGATION = {
  Sum: 0,
  Max: 1,
  LastValue: 2,
} as const;

export const ENTITLEMENT_LIMIT_KIND = {
  Boolean: 0,
  Count: 1,
  Unlimited: 2,
} as const;

export type BillingIntervalName = keyof typeof BILLING_INTERVAL;
export type MeterAggregationName = keyof typeof METER_AGGREGATION;
export type EntitlementLimitKindName = keyof typeof ENTITLEMENT_LIMIT_KIND;

export interface PlanQuantityItem {
  itemKey: string;
  unitLabel: string;
  minQuantity: number;
  maxQuantity: number | null;
  defaultQuantity: number;
}

export interface MeterTier {
  upToQuantity: number | null;
  unitAmountMinor: number;
}

export interface MeterRateTable {
  currencyCode: string;
  tiers: MeterTier[];
}

export interface PlanMeter {
  meterKey: string;
  displayName: string;
  unitLabel: string;
  /** Response DTOs carry this as a string name (e.g. "Sum"); requests send the numeric value. */
  aggregation: MeterAggregationName;
  includedQuantity: number;
  overageAllowed: boolean;
  thresholdPercents: number[];
  /**
   * Empty when overage cannot be priced. Optional rather than required because a plan stored
   * before the response carried this field has no array at all, and reading length off that
   * took the whole detail page down.
   */
  rateTables?: MeterRateTable[];
}

export interface PlanEntitlement {
  key: string;
  /** Response DTOs carry this as a string name (e.g. "Count"); requests send the numeric value. */
  limitKind: EntitlementLimitKindName;
  limit: number | null;
  meterKey: string | null;
  unitLabel: string | null;
}

export interface PlanPrice {
  priceId: string;
  currencyCode: string;
  unitAmountMinor: number;
  /** Response DTOs carry this as a string name (e.g. "Month"); requests send the numeric value. */
  interval: BillingIntervalName;
  intervalCount: number;
  quantityItemKey: string | null;
}

export interface SubscriptionPlan {
  planId: string;
  code: string;
  displayName: string;
  description: string | null;
  featuresJson: string | null;
  organizationId: string | null;
  trialDays: number | null;
  trialRequiresPaymentMethod: boolean;
  version: number;
  quantityItems: PlanQuantityItem[];
  meters: PlanMeter[];
  entitlements: PlanEntitlement[];
  prices: PlanPrice[];
}

export interface CreatePlanQuantityItemRequest {
  itemKey: string;
  unitLabel: string;
  minQuantity: number;
  maxQuantity?: number;
  defaultQuantity: number;
}

export interface CreatePlanMeterRequest {
  meterKey: string;
  displayName: string;
  unitLabel: string;
  aggregation: number;
  includedQuantity: number;
  overageAllowed: boolean;
  thresholdPercents: number[];
  rateTables: {
    currencyCode: string;
    tiers: { upToQuantity?: number; unitAmountMinor: number }[];
  }[];
}

export interface CreatePlanEntitlementRequest {
  key: string;
  limitKind: number;
  limit?: number;
  meterKey?: string;
  unitLabel?: string;
}

export interface CreatePlanTrialGrantRequest {
  meterKey: string;
  includedQuantity: number;
}

export interface CreateSubscriptionPlanRequest {
  code: string;
  displayName: string;
  description?: string;
  featuresJson?: string;
  /** Omitted entirely for a tenant-wide plan. */
  organizationId?: string;
  trialDays?: number;
  trialRequiresPaymentMethod: boolean;
  quantityItems: CreatePlanQuantityItemRequest[];
  meters: CreatePlanMeterRequest[];
  entitlements: CreatePlanEntitlementRequest[];
  trialGrants: CreatePlanTrialGrantRequest[];
}

export interface CreateSubscriptionPriceRequest {
  planId: string;
  /** Omitted for a tenant-wide plan; honoured for the console only. */
  organizationId?: string;
  currencyCode: string;
  unitAmountMinor: number;
  interval: number;
  intervalCount: number;
  quantityItemKey?: string;
}
