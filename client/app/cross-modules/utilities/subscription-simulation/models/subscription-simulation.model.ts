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
  /** Present on reads, absent on writes: the server owns the label. */
  unitLabel?: string;
}

/**
 * The volume band a quantity falls in.
 *
 * Read, never chosen. A client that sent a band would be naming a price the plan may not agree
 * to; the quantity is the input and the band is the consequence.
 */
export interface QuantityDiscountTier {
  minimumQuantity: number;
  maximumQuantity: number | null;
  discountBasisPoints: number;
}

/** A reduction already booked for the end of the paid period. */
export interface PendingQuantityChange {
  quantities: SubscriptionQuantity[];
  requestedAtUtc: string;
  effectiveAtUtc: string;
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
  /**
   * A decrease waiting for the paid period to end, if one is scheduled. The quantities above are
   * still what the subscriber holds and pays for until then.
   */
  pendingQuantityChange: PendingQuantityChange | null;
  /** The band the quantity in force selects, if the plan defines any. */
  currentTier: QuantityDiscountTier | null;
  /** What the next renewal costs at the quantity, band and discount in force. */
  recurringAmountMinor: number;
  /** Only present while payment is outstanding; null once activated. */
  checkoutUrl: string | null;
  version: number;
}

/**
 * A requested quantity, and the version it was quoted against.
 *
 * The version is required: without it a stale tab can overwrite a seat count somebody else moved
 * a minute ago. A request naming one item leaves the others alone.
 */
export interface ChangeQuantityRequest {
  version: number;
  quantities: { itemKey: string; quantity: number }[];
  /**
   * Which organization the subscription belongs to. Carried for the same reason subscribing
   * carries it: this portal calls the API as the console acting on a chosen organization's behalf,
   * and without it every quantity call resolves against the caller's own organization and answers
   * "subscription not found". Ignored for an ordinary integrator's token, whose scope is its own.
   */
  organizationId?: string;
}

/**
 * What a quantity change costs and when it takes effect — the same shape whether it was previewed
 * or applied, so a confirmation screen and its outcome are rendered from one set of fields.
 */
export interface QuantityChangeQuote {
  subscriptionId: string;
  /** The version after the change. Unchanged on a preview. */
  version: number;
  preview: boolean;
  /** <code>Immediate</code> for an increase, <code>NextPeriod</code> for a decrease. */
  timing: "Immediate" | "NextPeriod";
  effectiveAtUtc: string;
  quantities: SubscriptionQuantity[];
  currentTier: QuantityDiscountTier | null;
  targetTier: QuantityDiscountTier | null;
  /** Owed now for the rest of the period. Zero for a decrease, which is never refunded. */
  proratedChargeMinor: number;
  nextRenewalAmountMinor: number;
  /**
   * What one unit costs at the target quantity, before tax, as stated by the server. Null when the
   * price is a flat fee and there is no such thing as a unit price.
   *
   * Never recomputed here. The band alone does not determine it — a promotion on the subscription,
   * the plan's combination policy and the server's rounding all move it — so a percentage applied
   * to the list price in the browser can disagree with the charge being confirmed.
   */
  effectiveUnitAmountMinor: number | null;
  /** The tax inside `nextRenewalAmountMinor`, which is the tax-inclusive total. */
  taxAmountMinor: number;
  /** Whether a promotional discount is part of these figures. */
  promotionApplied: boolean;
  currencyCode: string;
  chargePaymentDetailId: string | null;
  pendingQuantityChange: PendingQuantityChange | null;
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
  /** Honoured only for this portal acting as the platform console; see SubscribeToPlanRequest. */
  organizationId?: string;
}

/**
 * The server looks the target plan up by `planCode`, then requires `priceId` to belong to that
 * plan (mismatch is `subscription_price_not_found`) — despite what the integration guide says,
 * naming only the price is not enough; the plan the price sits on must be named too.
 */
export interface ChangeSubscriptionPlanRequest {
  planCode: string;
  priceId: string;
  quantities: SubscriptionQuantity[];
  /** Honoured only for this portal acting as the platform console; see SubscribeToPlanRequest. */
  organizationId?: string;
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

/**
 * One step of the immutable lifecycle trail kept for investigating a subscription.
 *
 * Deliberately thin: the server never returns an actor id or a payment id here, so this can be
 * shown to whoever is looking at the simulation without exposing who did it or linking straight
 * to a payment record. `operation`, `stage`, `outcome`, `source` and `failureKind` are free-form
 * strings owned by the server (worker-raised events use different values than API-raised ones),
 * not a closed set the client can validate against.
 */
export interface SubscriptionAuditEvent {
  eventId: string;
  operationId: string;
  correlationId: string;
  operation: string;
  stage: string;
  outcome: string;
  source: string;
  amountMinor: number | null;
  currencyCode: string | null;
  fromStatus: string | null;
  toStatus: string | null;
  errorCode: string | null;
  failureKind: string | null;
  attempt: number | null;
  occurredAtUtc: string;
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
