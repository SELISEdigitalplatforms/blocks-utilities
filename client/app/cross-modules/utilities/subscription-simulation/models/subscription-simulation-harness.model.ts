/**
 * Types for the test-harness controller (`/api/subscription-simulation/...`) — distinct from
 * `subscription-simulation.model.ts`, which mirrors the integrator-facing API a real application
 * would call. Naming mirrors the server's C# types exactly so the two stay easy to cross-reference.
 */

export type SubscriptionPaymentPurpose = "InitialCharge" | "Renewal";

export type SimulatedPaymentFailureKind =
  | "Declined"
  | "InsufficientFunds"
  | "PaymentMethodExpired"
  | "ProviderUnavailable"
  | "OutcomeUnknown";

/** `Succeeded` plus every `SimulatedPaymentFailureKind` value — the outcome vocabulary a renewal or a usage charge scripts. */
export type SimulatedRenewalOutcome = "Succeeded" | SimulatedPaymentFailureKind;

export type SimulationWorkType =
  | "Renewal"
  | "UsagePeriodClosure"
  | "UsageInvoiceCharge"
  | "OutboxPublication";

export interface MarkPaymentSucceededRequest {
  organizationId?: string;
  paymentPurpose: SubscriptionPaymentPurpose;
  providerReference?: string;
  /** Defaults to true server-side when omitted. */
  runProcessor?: boolean;
}

export interface MarkPaymentFailedRequest {
  organizationId?: string;
  paymentPurpose: SubscriptionPaymentPurpose;
  failureKind: SimulatedPaymentFailureKind;
  errorCode?: string;
  runProcessor?: boolean;
}

export interface AdvanceRenewalRequest {
  organizationId?: string;
  paymentOutcome: SimulatedRenewalOutcome;
}

export interface CloseUsagePeriodRequest {
  organizationId?: string;
  paymentOutcome?: SimulatedRenewalOutcome;
  chargeInvoice?: boolean;
}

export interface RunDueJobsRequest {
  organizationId?: string;
  /** Empty means every work type the endpoint knows about. */
  workTypes?: SimulationWorkType[];
}

/** The handful of fields worth comparing before and after an action, without repeating the entire state twice. */
export interface SubscriptionSimulationSummary {
  subscriptionStatus: string;
  currentPeriodEndUtc: string | null;
  nextFeeBillingAtUtc: string | null;
  dunningAttemptCount: number;
  lastRenewalPaymentDetailId: string | null;
  version: number;
}

/** What a simulation mutation did, alongside a before/after comparison. */
export interface SubscriptionSimulationActionResponse {
  simulationRunId: string;
  /** E.g. `MarkPaymentSucceeded`, `MarkPaymentFailed`. */
  action: string;
  startedAtUtc: string;
  completedAtUtc: string;
  before: SubscriptionSimulationSummary;
  after: SubscriptionSimulationSummary;
  correlationId: string;
}

export interface SubscriptionSimulationJobResultResponse {
  workType: SimulationWorkType;
  /** `Completed`, `NotDue` or `NotApplicable` (the subscription's own state rules it out entirely). */
  status: string;
  detail: string | null;
  durationMs: number;
}

/** What running due background work for one subscription actually did. */
export interface SubscriptionSimulationJobRunResponse {
  simulationRunId: string;
  startedAtUtc: string;
  completedAtUtc: string;
  claimed: number;
  completed: number;
  /** Work types that were asked for but were not actually due — not a failure. */
  notDue: number;
  jobs: SubscriptionSimulationJobResultResponse[];
  correlationId: string;
}
