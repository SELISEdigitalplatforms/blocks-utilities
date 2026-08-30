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
  /** Card-only checkout state; setup never represents money moving. */
  pendingCheckout?: {
    purpose: "PaymentMethodSetup";
    state: "Pending" | "Failed" | "Expired";
    errorCode: string | null;
    checkoutUrl: string | null;
  } | null;
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

/**
 * What confirming would refuse, seen alongside the price rather than instead of it.
 *
 * The same error codes a failed subscribe returns, so a screen already handling those learns no
 * second vocabulary for the preview.
 */
export interface SubscriptionPreviewBlocker {
  code: string;
  message: string;
  /** Set only for `subscription_billing_profile_incomplete`. */
  fields?: Record<string, string[]> | null;
}

export interface SubscriptionPreviewAnnualPeriod {
  startUtc: string;
  endUtc: string;
  amountMinor: number;
  netAmountMinor: number;
  taxAmountMinor: number;
  /** Whether the year's amount is already included in `totalDueNowMinor`. */
  collectedWithCheckout: boolean;
}

/**
 * Why this quote is temporary, present only when the discount code applied is a platform
 * campaign rather than an ordinary promotional code. Every other field on the preview already
 * carries the right numbers for a campaign — this is only the "why" behind them.
 */
export interface SubscriptionPreviewCampaign {
  kind: "FreeOpeningCalendarPeriod" | "FirstAnnualPeriod";
  /** A short, ready-to-display sentence explaining the offer and when it ends. */
  description: string;
  /** The instant standard pricing resumes. */
  discountEndsAtUtc: string;
  /** Set only for a free-opening-period campaign that caps an entitlement while it runs. */
  temporaryEntitlementKey: string | null;
  temporaryEntitlementLimit: number | null;
}

/**
 * What subscribing would cost right now, and what would stand in the way — without subscribing.
 *
 * `totalDueNowMinor` is the exact figure a confirming subscribe call then charges: both read the
 * same frozen amount on the server, so this cannot quote one number and charge another.
 */
export interface SubscriptionPurchasePreview {
  currencyCode: string;
  subtotalMinor: number;
  /** Every reduction combined. */
  discountMinor: number;
  builtInDiscountMinor: number;
  promotionalDiscountMinor: number;
  taxMinor: number;
  /** What confirming this preview would actually charge. Zero for a card-free trial. */
  totalDueNowMinor: number;
  prorated: boolean;
  coveredDays: number | null;
  totalDays: number | null;
  periodStartUtc: string;
  periodEndUtc: string;
  nextRenewalAtUtc: string | null;
  nextRenewalAmountMinor: number;
  /** Set only for a subscription that opens on a trial. */
  trialEndsAtUtc: string | null;
  /** Whether confirming will ask for a card even though nothing is due now. */
  requiresCardSetup: boolean;
  pendingAnnualPeriod: SubscriptionPreviewAnnualPeriod | null;
  /** Null unless the discount code applied is a platform campaign. */
  campaign: SubscriptionPreviewCampaign | null;
  /** Empty when nothing stands in the way of confirming. */
  blockers: SubscriptionPreviewBlocker[];
  /** The instant these figures were derived from. */
  quotedAtUtc: string;
  /**
   * The earliest instant this quote's proration could no longer hold. Null when nothing here is
   * prorated, because then no boundary changes the answer.
   */
  quoteValidUntilUtc: string | null;
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

/** One side of a plan-change settlement — what a period costs, and how much of it this counts. */
export interface PlanChangeSettlementSide {
  grossAmountMinor: number;
  builtInDiscountMinor: number;
  promotionalDiscountMinor: number;
  taxAmountMinor: number;
  /** The whole period, tax included — undiminished by proration. */
  periodTotalMinor: number;
  /** The part of the period this settlement actually counts. */
  proratedValueMinor: number;
}

export interface PlanChangeSettlement {
  outgoing: PlanChangeSettlementSide;
  target: PlanChangeSettlementSide;
  creditConsumedMinor: number;
  netSettlementMinor: number;
}

/**
 * What moving to another plan or price would cost or credit right now, without applying anything.
 *
 * Unlike {@link SubscriptionPurchasePreview}, nothing here is frozen ahead of time — a plan
 * change is priced fresh, immediately before it is applied, every time it runs. So this quote
 * holds only up to the clock: re-fetch it immediately before confirming rather than holding it.
 */
export interface SubscriptionPlanChangePreview {
  currencyCode: string;
  targetPlanCode: string;
  targetPlanName: string;
  targetPriceId: string;
  interval: string;
  intervalCount: number;
  quantities: SubscriptionQuantity[];
  /** What confirming this preview would charge now. Zero for a downgrade. */
  chargeMinor: number;
  /** What confirming this preview would bank as credit. Zero for an upgrade. */
  creditBankedMinor: number;
  settlement: PlanChangeSettlement;
  newPeriodStartUtc: string;
  newPeriodEndUtc: string;
  nextRenewalAmountMinor: number;
  blockers: SubscriptionPreviewBlocker[];
  quotedAtUtc: string;
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

/**
 * A hypothetical slice of additional metered usage to price. Writes nothing and charges nothing —
 * see {@link UsageOveragePreviewResult}.
 */
export interface PreviewUsageOverageRequest {
  meterKey: string;
  /** Cannot be zero or negative — a preview of no additional usage answers nothing. */
  additionalQuantity: number;
  /** Honoured only for this portal acting as the platform console; see SubscribeToPlanRequest. */
  organizationId?: string;
}

/** One charge, split the way an invoice line would be. All amounts are minor units. */
export interface UsageChargeAmounts {
  grossMinor: number;
  automaticDiscountMinor: number;
  netMinor: number;
  taxMinor: number;
  totalMinor: number;
}

/** One graduated tier band's slice of the additional usage. */
export interface UsageOverageTierAllocation {
  /**
   * The first overage unit this band covers, counted from the first overage unit of the whole
   * period — not from wherever the additional usage itself begins.
   */
  fromOverageQuantity: number;
  toOverageQuantity: number;
  units: number;
  unitAmountMinor: number;
  amountMinor: number;
}

export interface UsageOveragePreviewDiscount {
  automaticBasisPoints: number;
  /** Always false — a promotional discount code never reduces metered overage. */
  promotionalCodeApplied: boolean;
}

export interface UsageOveragePreviewTax {
  rateBasisPoints: number | null;
  mode: string;
}

/**
 * What a hypothetical slice of additional metered usage would cost, rated with the subscription's
 * own snapshotted terms and the same order of operations period-end usage rating uses — but
 * nothing here is recorded and nothing here is charged. Server-computed throughout; nothing here
 * should be recalculated in the browser.
 */
export interface UsageOveragePreviewResult {
  meterKey: string;
  unitLabel: string;
  currencyCode: string;
  periodKey: string;
  periodStartUtc: string;
  periodEndUtc: string;
  /** When this preview was computed. Usage recorded afterward can change the eventual invoice. */
  calculatedAtUtc: string;
  includedQuantity: number;
  currentUsage: number;
  currentOverage: number;
  additionalQuantity: number;
  projectedUsage: number;
  projectedOverage: number;
  currentCharge: UsageChargeAmounts;
  /** The difference between the projected and current charges — never rated on its own. */
  additionalCharge: UsageChargeAmounts;
  projectedPeriodCharge: UsageChargeAmounts;
  additionalTierBreakdown: UsageOverageTierAllocation[];
  discount: UsageOveragePreviewDiscount;
  tax: UsageOveragePreviewTax;
  writesUsage: boolean;
  chargesPayment: boolean;
  finalChargeDependsOnActualPeriodEndUsage: boolean;
}
