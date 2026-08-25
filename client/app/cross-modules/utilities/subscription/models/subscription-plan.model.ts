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

export const METER_RESET_POLICY = {
  Periodic: 0,
  Never: 1,
  CarryForward: 2,
} as const;

export const ENTITLEMENT_LIMIT_KIND = {
  Boolean: 0,
  Count: 1,
  Unlimited: 2,
} as const;

/** Indexed by the numeric value a form holds, so a draft can be read the way a response reads. */
export const ENTITLEMENT_LIMIT_KIND_NAMES = ["Boolean", "Count", "Unlimited"] as const;

export const BILLING_INTERVAL_NAMES = ["Day", "Week", "Month", "Year"] as const;

export type BillingIntervalName = keyof typeof BILLING_INTERVAL;
export type MeterAggregationName = keyof typeof METER_AGGREGATION;
export type MeterResetPolicyName = keyof typeof METER_RESET_POLICY;
export type EntitlementLimitKindName = keyof typeof ENTITLEMENT_LIMIT_KIND;

/**
 * A volume band: how much is taken off when the quantity held falls inside it.
 *
 * Volume pricing, not graduated pricing. The band is chosen by the whole quantity and its
 * discount applies to the whole charge — 10 users in a 10% band is 10 users at 10% off, not
 * four at full price and six discounted.
 */
/**
 * What happens when a subscriber holds both a volume band and a promotional code.
 *
 * A commercial choice, not an arithmetic one, which is why it is authored rather than inferred:
 * <code>Stack</code> compounds, and a plan that meant to compound and was quietly reset to
 * <code>BestDiscount</code> gives away a different amount of money every month.
 */
export type QuantityDiscountCombinationPolicyName = "BestDiscount" | "QuantityOnly" | "Stack";

export interface QuantityDiscountTier {
  minimumQuantity: number;
  /** Null on the last band of an unbounded item: everything above the minimum falls in it. */
  maximumQuantity: number | null;
  /** 500 is 5%. Basis points on the wire; the builder edits percentages. */
  discountBasisPoints: number;
}

export interface PlanQuantityItem {
  itemKey: string;
  unitLabel: string;
  minQuantity: number;
  maxQuantity: number | null;
  defaultQuantity: number;
  /** Optional for the same reason trial grants are: plans stored before this lack it. */
  quantityDiscountTiers?: QuantityDiscountTier[];
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
  /**
   * Periodic meters reset with the plan window, Never meters keep one lifetime balance, and
   * CarryForward meters reset but open with whatever the previous window left.
   */
  resetPolicy?: MeterResetPolicyName;
  /** The most one window may carry in. Present only on a carry-forward meter. */
  carryForwardCap?: number | null;
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
  displayPriceNote?: string | null;
  quantityItemKey: string | null;
  /** Basis points — 770 is 7.7%. Absent when the price carries no tax. */
  taxRateBasisPoints?: number | null;
  /**
   * "Exclusive" or "Inclusive". Present for any taxed price, including those authored before modes
   * existed — the server reports those as exclusive, which is how they are charged.
   */
  taxMode?: string | null;
  /** Basis points off without a code — 800 is 8%. Absent when the price has no automatic discount. */
  automaticDiscountBasisPoints?: number | null;
  /**
   * "BestDiscount" or "Additive" — how that discount meets a volume band. Present whenever there is
   * an automatic discount; the server reports one authored without a combination as BestDiscount,
   * which is how it is calculated.
   */
  quantityDiscountCombination?: string | null;
}

export interface SubscriptionPlan {
  planId: string;
  code: string;
  displayName: string;
  description: string | null;
  familyCode?: string | null;
  familyRank?: number | null;
  usageInterval?: BillingIntervalName;
  usageIntervalCount?: number;
  /**
   * How a volume band and a promotional code combine. A name on the wire, like every other enum
   * in a plan response, and absent on plans stored before bands existed.
   */
  quantityDiscountCombinationPolicy?: QuantityDiscountCombinationPolicyName;
  featuresJson: string | null;
  organizationId: string | null;
  trialDays: number | null;
  trialRequiresPaymentMethod: boolean;
  version: number;
  /**
   * Whether anything has ever subscribed to this plan. True closes editing: a subscription bills
   * from its own copy of the plan's terms, which an edit cannot reach.
   */
  hasSubscribers: boolean;
  quantityItems: PlanQuantityItem[];
  meters: PlanMeter[];
  entitlements: PlanEntitlement[];
  prices: PlanPrice[];
  /** Optional for the same reason rate tables are: plans stored before this was returned lack it. */
  trialGrants?: PlanTrialGrant[];
}

export interface PlanTrialGrant {
  meterKey: string;
  includedQuantity: number;
}

export interface CreatePlanQuantityItemRequest {
  itemKey: string;
  unitLabel: string;
  minQuantity: number;
  maxQuantity?: number;
  defaultQuantity: number;
  /** Omitted rather than sent empty, so a plan with no bands stays a plan with no bands. */
  quantityDiscountTiers?: CreateQuantityDiscountTierRequest[];
}

export interface CreateQuantityDiscountTierRequest {
  minimumQuantity: number;
  maximumQuantity?: number;
  discountBasisPoints: number;
}

export interface CreatePlanMeterRequest {
  meterKey: string;
  displayName: string;
  unitLabel: string;
  aggregation: number;
  resetPolicy: number;
  carryForwardCap?: number;
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
  /** Sent on every write: omitted, the server would reset it to BestDiscount. */
  quantityDiscountCombinationPolicy: number;
  usageInterval: number;
  usageIntervalCount: number;
  familyCode?: string;
  familyRank?: number;
  quantityItems: CreatePlanQuantityItemRequest[];
  meters: CreatePlanMeterRequest[];
  entitlements: CreatePlanEntitlementRequest[];
  trialGrants: CreatePlanTrialGrantRequest[];
}

/**
 * Rewrites what a plan sells. Carries no code and no scope: the server takes both from the stored
 * plan, because neither may move once anything points at it.
 */
export interface UpdateSubscriptionPlanRequest {
  displayName: string;
  description?: string;
  featuresJson?: string;
  /** Names the plan's organization for the console. It never changes the plan's scope. */
  organizationId?: string;
  trialDays?: number;
  trialRequiresPaymentMethod: boolean;
  /** Sent on every write: omitted, the server would reset it to BestDiscount. */
  quantityDiscountCombinationPolicy: number;
  usageInterval: number;
  usageIntervalCount: number;
  familyCode?: string;
  familyRank?: number;
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
  displayPriceNote?: string;
  quantityItemKey?: string;
  /** Basis points. Omitted for an untaxed price; the mode is required whenever this is positive. */
  taxRateBasisPoints?: number;
  taxMode?: string;
  /** Basis points off without a code. Omitted for no automatic discount. */
  automaticDiscountBasisPoints?: number;
  /** Omitted reads as "BestDiscount" on the server — the answer that gives away less. */
  quantityDiscountCombination?: string;
}

/**
 * Changes what an existing price takes off automatically. Reaches future subscriptions and future
 * moves onto the price only — everyone already on it keeps the terms they were sold.
 */
export interface UpdateSubscriptionPriceDiscountRequest {
  organizationId?: string;
  /** Zero clears the discount. */
  automaticDiscountBasisPoints?: number;
  quantityDiscountCombination?: "BestDiscount" | "Additive";
}

export interface UpdateSubscriptionPriceTaxRequest {
  organizationId?: string;
  taxRateBasisPoints?: number;
  taxMode?: "Exclusive" | "Inclusive";
}

export interface SubscriptionDiscount {
  discountId: string;
  organizationId: string | null;
  code: string;
  displayName: string;
  kind: "Percent" | "FixedAmount";
  percentBasisPoints: number | null;
  amountMinor: number | null;
  currencyCode: string | null;
  durationPeriods: number | null;
  expiresAtUtc: string | null;
  applicablePlanCodes: string[];
  /** Absent on discounts stored before price restrictions existed, which are unrestricted by price. */
  applicablePriceIds?: string[];
  status: "Active" | "Archived";
}

export interface CreateSubscriptionDiscountRequest {
  organizationId?: string;
  code: string;
  displayName: string;
  kind: number;
  percentBasisPoints?: number;
  amountMinor?: number;
  currencyCode?: string;
  durationPeriods?: number;
  expiresAtUtc?: string;
  applicablePlanCodes: string[];
  /** Narrows the plan list rather than replacing it: both have to match when both are given. */
  applicablePriceIds: string[];
}
