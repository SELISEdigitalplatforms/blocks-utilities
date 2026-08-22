import type { BillingIntervalName } from "../../subscription/models/subscription-plan.model";

export type SubscriptionStatus =
  | "Incomplete"
  | "IncompleteExpired"
  | "Trialing"
  | "Active"
  | "PastDue"
  | "Unpaid"
  | "Canceled";

export interface SubscriptionQuantity {
  itemKey: string;
  quantity: number;
}

export interface SimulatedSubscription {
  subscriptionId: string;
  status: SubscriptionStatus;
  planCode: string;
  planName: string;
  currencyCode: string;
  unitAmountMinor: number;
  interval: BillingIntervalName;
  intervalCount: number;
  displayPriceNote: string | null;
  quantities: SubscriptionQuantity[];
  currentPeriodStartUtc: string;
  currentPeriodEndUtc: string;
  nextPaymentAtUtc: string | null;
  trialEndsAtUtc: string | null;
  cancelAtPeriodEnd: boolean;
  canceledAtUtc: string | null;
  /** Only present while payment is outstanding; null once activated. */
  checkoutUrl: string | null;
  version: number;
}

export interface SubscribeToPlanRequest {
  /** The plan's stable code, not its planId — sending the id reads as "plan not found". */
  planCode: string;
  priceId: string;
  quantities: SubscriptionQuantity[];
  timeZoneId: string;
  discountCode?: string;
  billingEmail?: string;
  billingName?: string;
  /**
   * Honoured only because this portal calls the API as the platform console acting on the
   * chosen organization's behalf. Omitted entirely for the tenant-wide scope, the same way plan
   * authoring omits it — an ordinary integrator's token already carries its own organization and
   * this field is silently ignored for it.
   */
  organizationId?: string;
}

export interface CancelSubscriptionRequest {
  subscriptionId: string;
  /** Default false — keeps granting until the paid period ends. True stops right away. */
  immediately: boolean;
  reason?: string;
}

/**
 * A client never sends a target planCode. It sends the target price; the server follows
 * `priceId → Price.PlanId → Plan` on its own, applies the new snapshots atomically, and handles
 * proration.
 */
export interface ChangeSubscriptionPlanRequest {
  priceId: string;
  quantities: SubscriptionQuantity[];
}

export type PlanChangeLabel =
  | "Upgrade"
  | "Downgrade"
  | "Switch plan"
  | "Change billing cadence";

/**
 * A single entitlement's live decision. Reading one is a check, not enforcement — two callers
 * both a unit under the limit can both read `allowed: true`, and only the usage call below tells
 * them apart. `used`/`remaining` are present only for a `Count` entitlement.
 */
export interface EntitlementDecision {
  key: string;
  allowed: boolean;
  reason: "Allowed" | "NoSubscription" | "SubscriptionNotActive" | "NotInPlan" | "LimitReached";
  limitKind: "Boolean" | "Count" | "Unlimited";
  limit: number | null;
  used?: number | null;
  remaining?: number | null;
  unitLabel: string | null;
}

export interface EntitlementsSnapshot {
  hasSubscription: boolean;
  status?: SubscriptionStatus;
  planCode?: string;
  currentPeriodEndUtc?: string | null;
  trialEndsAtUtc?: string | null;
  quantities: SubscriptionQuantity[];
  entitlements: EntitlementDecision[];
  featuresJson: string | null;
}

export interface RecordUsageRequest {
  meterKey: string;
  quantity: number;
  /** Mandatory — at-least-once delivery makes a retried call a certainty, and it must not double-count. */
  idempotencyKey: string;
  /** Refuses and rolls back once the allowance is exhausted, but only for a meter with no overage. */
  enforce: boolean;
  /** Honoured only for this portal acting as the platform console; see SubscribeToPlanRequest. */
  organizationId?: string;
}

export interface RecordUsageResult {
  allowed: boolean;
  meterKey: string;
  unitLabel: string;
  periodKey: string;
  periodStartUtc: string;
  periodEndUtc: string;
  included: number;
  used: number;
  remaining: number;
  overage: number;
  replayed: boolean;
}
