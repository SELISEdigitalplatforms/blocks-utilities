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

/**
 * The data console — a narrow, allowlisted read/update surface over Mongo for whatever the
 * scripted actions above cannot reach. Never a raw query: every call below is scoped server-side
 * to one collection from this allowlist, one tenant, one organization and one subscription.
 */
export interface SubscriptionSimulationDataPolicyResponse {
  logicalName: string;
  canRead: boolean;
  /** Always false in this version — see the server's policy remarks for why. */
  canInsert: boolean;
  /** Field names `update` may set on this collection — always parsed as UTC timestamps. */
  updatableFields: string[];
}

export interface FindDataRequest {
  organizationId?: string;
  subscriptionId: string;
  limit?: number;
}

export interface UpdateDataFieldRequest {
  organizationId?: string;
  subscriptionId: string;
  /** Field name to an ISO 8601 UTC timestamp string. */
  fields: Record<string, string>;
}

export interface SubscriptionSimulationDataQueryResponse {
  collection: string;
  count: number;
  /** Each document as a JSON string, already redacted the same way the state endpoint is. */
  documents: string[];
  correlationId: string;
}

export interface SubscriptionSimulationDataMutationResponse {
  collection: string;
  modified: boolean;
  fieldsSet: string[];
  correlationId: string;
}
