import { HttpError, type RequestBody } from "@seliseblocks/genesis-os/lib";
import { serviceInstances } from "@/lib/http-client";
import { SIMULATION_HARNESS_ENDPOINT } from "../constants/subscription-simulation.constants";
import type {
  AdvanceRenewalRequest,
  CloseUsagePeriodRequest,
  FindDataRequest,
  MarkPaymentFailedRequest,
  MarkPaymentSucceededRequest,
  RunDueJobsRequest,
  SubscriptionSimulationActionResponse,
  SubscriptionSimulationDataMutationResponse,
  SubscriptionSimulationDataPolicyResponse,
  SubscriptionSimulationDataQueryResponse,
  SubscriptionSimulationJobRunResponse,
  UpdateDataFieldRequest,
} from "../models/subscription-simulation-harness.model";

interface HarnessApiError {
  code: string;
  message: string;
  fields?: Record<string, string[]> | null;
}

interface HarnessApiResponse<T> {
  success: boolean;
  data: T | null;
  error: HarnessApiError | null;
}

/**
 * A failed harness call, with the server's own error code kept — in particular
 * `subscription_simulation_forbidden`, which the controller reports as a real 403 distinct from
 * every other outcome (which otherwise reads like the subscription itself was not found).
 */
export class SubscriptionSimulationHarnessError extends Error {
  constructor(
    message: string,
    readonly code: string,
    readonly status?: number,
  ) {
    super(message);
    this.name = "SubscriptionSimulationHarnessError";
  }
}

/**
 * Known error codes, searched for in the thrown error the same way the integrator-facing
 * service's `quantityErrorCode` does — the envelope lands in different shapes depending on the
 * HTTP status, so a code read from one fixed path is silently "unknown" for the others.
 */
const HARNESS_ERROR_CODES = [
  "subscription_simulation_disabled",
  "subscription_simulation_data_console_disabled",
  "subscription_simulation_forbidden",
  "subscription_simulation_not_found",
  "subscription_simulation_organization_required",
  "subscription_simulation_scheduling_not_supported",
  "subscription_simulation_periods_not_supported",
  "subscription_simulation_collection_not_allowed",
  "subscription_simulation_collection_not_readable",
  "subscription_simulation_limit_invalid",
  "subscription_simulation_no_fields",
  "subscription_simulation_field_not_allowed",
  "subscription_simulation_field_value_invalid",
] as const;

const serialize = (error: unknown): string => {
  if (typeof error === "string") {
    return error;
  }

  try {
    return JSON.stringify(error, [
      "message",
      "code",
      "error",
      "errors",
      "data",
      "detail",
      "title",
    ] as never);
  } catch {
    return String(error);
  }
};

const harnessErrorCode = (error: unknown): string => {
  const haystack = `${serialize(error)} ${error instanceof Error ? error.message : ""}`;

  return HARNESS_ERROR_CODES.find((code) => haystack.includes(code)) ?? "unknown";
};

const messageFrom = (error: unknown, fallback: string): string => {
  if (error instanceof HttpError) {
    const values = Object.values(error.errors ?? {}).flat();
    const first = values.find((value) => typeof value === "string" && value.trim().length > 0);

    if (first) {
      return first;
    }
  }

  return error instanceof Error && error.message ? error.message : fallback;
};

class SubscriptionSimulationHarnessService {
  async markPaymentSucceeded(
    subscriptionId: string,
    request: MarkPaymentSucceededRequest,
  ): Promise<SubscriptionSimulationActionResponse> {
    return this.post(
      `${this.subscriptionPath(subscriptionId)}/mark-payment-succeeded`,
      request,
      "The payment outcome could not be simulated.",
    );
  }

  async markPaymentFailed(
    subscriptionId: string,
    request: MarkPaymentFailedRequest,
  ): Promise<SubscriptionSimulationActionResponse> {
    return this.post(
      `${this.subscriptionPath(subscriptionId)}/mark-payment-failed`,
      request,
      "The payment outcome could not be simulated.",
    );
  }

  async advanceRenewal(
    subscriptionId: string,
    request: AdvanceRenewalRequest,
  ): Promise<SubscriptionSimulationActionResponse> {
    return this.post(
      `${this.subscriptionPath(subscriptionId)}/advance-renewal`,
      request,
      "The renewal could not be advanced.",
    );
  }

  async closeUsagePeriod(
    subscriptionId: string,
    request: CloseUsagePeriodRequest,
  ): Promise<SubscriptionSimulationActionResponse> {
    return this.post(
      `${this.subscriptionPath(subscriptionId)}/close-usage-period`,
      request,
      "The usage period could not be closed.",
    );
  }

  async runDueJobs(
    subscriptionId: string,
    request: RunDueJobsRequest,
  ): Promise<SubscriptionSimulationJobRunResponse> {
    return this.post(
      `${this.subscriptionPath(subscriptionId)}/jobs/run-due`,
      request,
      "The due background work could not be run.",
    );
  }

  /**
   * The allowlisted collections and what the console may do to each — for a caller deciding what
   * `find`/`update` can reach before trying either.
   */
  async getDataPolicy(): Promise<SubscriptionSimulationDataPolicyResponse[]> {
    return this.get(`${SIMULATION_HARNESS_ENDPOINT}/data/policy`, "The data console policy could not be loaded.");
  }

  async findData(
    subscriptionId: string,
    logicalCollection: string,
    request: FindDataRequest,
  ): Promise<SubscriptionSimulationDataQueryResponse> {
    return this.post(
      `${this.dataPath(subscriptionId, logicalCollection)}/find`,
      request,
      "The data could not be read.",
    );
  }

  async updateData(
    subscriptionId: string,
    logicalCollection: string,
    request: UpdateDataFieldRequest,
  ): Promise<SubscriptionSimulationDataMutationResponse> {
    return this.post(
      `${this.dataPath(subscriptionId, logicalCollection)}/update`,
      request,
      "The field could not be updated.",
    );
  }

  private subscriptionPath(subscriptionId: string): string {
    return `${SIMULATION_HARNESS_ENDPOINT}/subscriptions/${encodeURIComponent(subscriptionId)}`;
  }

  private dataPath(subscriptionId: string, logicalCollection: string): string {
    return `${this.subscriptionPath(subscriptionId)}/data/${encodeURIComponent(logicalCollection)}`;
  }

  private async get<TResponse>(path: string, fallbackMessage: string): Promise<TResponse> {
    try {
      const response = await serviceInstances.utitlitiesService.get<
        HarnessApiResponse<TResponse>
      >(path);

      if (!response.success || !response.data) {
        throw new SubscriptionSimulationHarnessError(
          response.error?.message || fallbackMessage,
          response.error?.code ?? "unknown",
        );
      }

      return response.data;
    } catch (error) {
      if (error instanceof SubscriptionSimulationHarnessError) {
        throw error;
      }

      if (error instanceof HttpError) {
        throw new SubscriptionSimulationHarnessError(
          messageFrom(error, fallbackMessage),
          harnessErrorCode(error),
          error.status,
        );
      }

      throw error;
    }
  }

  private async post<TResponse, TRequest extends RequestBody>(
    path: string,
    request: TRequest,
    fallbackMessage: string,
  ): Promise<TResponse> {
    try {
      const response = await serviceInstances.utitlitiesService.post<
        HarnessApiResponse<TResponse>
      >(path, request);

      if (!response.success || !response.data) {
        throw new SubscriptionSimulationHarnessError(
          response.error?.message || fallbackMessage,
          response.error?.code ?? "unknown",
        );
      }

      return response.data;
    } catch (error) {
      if (error instanceof SubscriptionSimulationHarnessError) {
        throw error;
      }

      if (error instanceof HttpError) {
        throw new SubscriptionSimulationHarnessError(
          messageFrom(error, fallbackMessage),
          harnessErrorCode(error),
          error.status,
        );
      }

      throw error;
    }
  }
}

export const subscriptionSimulationHarnessService = new SubscriptionSimulationHarnessService();
