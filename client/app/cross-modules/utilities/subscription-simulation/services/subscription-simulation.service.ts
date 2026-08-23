import { HttpError } from "@seliseblocks/genesis-os/lib";
import { serviceInstances } from "@/lib/http-client";
import {
  ENTITLEMENTS_ENDPOINT,
  SUBSCRIPTION_USAGE_ENDPOINT,
  SUBSCRIPTIONS_CURRENT_ENDPOINT,
  SUBSCRIPTIONS_ENDPOINT,
} from "../constants/subscription-simulation.constants";
import type {
  CancelSubscriptionRequest,
  ChangeSubscriptionPlanRequest,
  EntitlementDecision,
  EntitlementsSnapshot,
  RecordUsageRequest,
  RecordUsageResult,
  SimulatedSubscription,
  SubscribeToPlanRequest,
} from "../models/subscription-simulation.model";

interface SimulationApiError {
  code: string;
  message: string;
  fields?: Record<string, string[]> | null;
}

interface SimulationApiResponse<T> {
  success: boolean;
  data: T | null;
  error: SimulationApiError | null;
}

class SubscriptionSimulationService {
  async getCurrentSubscription(
    organizationId?: string,
  ): Promise<SimulatedSubscription | null> {
    const query = organizationId
      ? `?organizationId=${encodeURIComponent(organizationId)}`
      : "";

    try {
      const response = await serviceInstances.utitlitiesService.get<
        SimulationApiResponse<SimulatedSubscription>
      >(`${SUBSCRIPTIONS_CURRENT_ENDPOINT}${query}`);

      return response.success ? response.data : null;
    } catch (error) {
      // No granting or pending subscription for this scope is an ordinary empty state for a
      // simulation screen, not a failure worth reporting as one.
      if (error instanceof HttpError && error.status === 404) {
        return null;
      }
      throw error;
    }
  }

  async subscribe(request: SubscribeToPlanRequest): Promise<SimulatedSubscription> {
    const response = await serviceInstances.utitlitiesService.post<
      SimulationApiResponse<SimulatedSubscription>
    >(SUBSCRIPTIONS_ENDPOINT, request);

    if (!response.success || !response.data) {
      throw new Error(response.error?.message || "The subscription could not be started.");
    }

    return response.data;
  }

  async cancel(request: CancelSubscriptionRequest): Promise<SimulatedSubscription> {
    const query = new URLSearchParams();
    query.set("immediately", String(request.immediately));
    if (request.reason) {
      query.set("reason", request.reason);
    }
    // Without this, the console falls back to its own organization's context, which does not
    // own the subscription being acted on — the lookup then 404s as "not found" rather than
    // "not yours" (see the docs' "A 404 may mean not yours").
    if (request.organizationId) {
      query.set("organizationId", request.organizationId);
    }

    const response = await serviceInstances.utitlitiesService.delete<
      SimulationApiResponse<SimulatedSubscription>
    >(`${SUBSCRIPTIONS_ENDPOINT}/${encodeURIComponent(request.subscriptionId)}?${query}`);

    if (!response.success || !response.data) {
      throw new Error(response.error?.message || "The subscription could not be canceled.");
    }

    return response.data;
  }

  /**
   * Moves an existing subscription to another price. The server resolves
   * `priceId → Price.PlanId → Plan` itself — the client never names a target plan directly.
   */
  async changePlan(
    subscriptionId: string,
    request: ChangeSubscriptionPlanRequest,
    organizationId?: string,
  ): Promise<SimulatedSubscription> {
    // Same reason as cancel(): resolving against the console's own organization rather than the
    // one the subscription actually belongs to reads back as "subscription not found".
    const query = organizationId
      ? `?organizationId=${encodeURIComponent(organizationId)}`
      : "";

    const response = await serviceInstances.utitlitiesService.put<
      SimulationApiResponse<SimulatedSubscription>
    >(`${SUBSCRIPTIONS_ENDPOINT}/${encodeURIComponent(subscriptionId)}/plan${query}`, request);

    if (!response.success || !response.data) {
      throw new Error(response.error?.message || "The plan could not be changed.");
    }

    return response.data;
  }

  /**
   * The cached-once-per-session read: every entitlement in one call, for the summary list.
   * `fresh` bypasses the short cache — counters are never cached regardless.
   */
  async getEntitlements(
    organizationId?: string,
    fresh = false,
  ): Promise<EntitlementsSnapshot> {
    const query = new URLSearchParams();
    if (organizationId) {
      query.set("organizationId", organizationId);
    }
    if (fresh) {
      query.set("fresh", "true");
    }
    const queryString = query.toString();
    const suffix = queryString ? `?${queryString}` : "";

    const response = await serviceInstances.utitlitiesService.get<
      SimulationApiResponse<EntitlementsSnapshot>
    >(`${ENTITLEMENTS_ENDPOINT}${suffix}`);

    if (!response.success || !response.data) {
      throw new Error(response.error?.message || "Entitlements could not be loaded.");
    }

    return response.data;
  }

  /**
   * The point-of-use read for one entitlement, always fresh. This is the "may they do this right
   * now" check a real integration runs immediately before letting an action proceed — it is
   * advisory, not enforcement, since nothing stops a second call from reading the same answer a
   * moment later.
   */
  async getEntitlement(
    entitlementKey: string,
    organizationId?: string,
  ): Promise<EntitlementDecision> {
    const query = organizationId
      ? `?organizationId=${encodeURIComponent(organizationId)}`
      : "";

    const response = await serviceInstances.utitlitiesService.get<
      SimulationApiResponse<EntitlementDecision>
    >(`${ENTITLEMENTS_ENDPOINT}/${encodeURIComponent(entitlementKey)}${query}`);

    if (!response.success || !response.data) {
      throw new Error(response.error?.message || "The entitlement could not be checked.");
    }

    return response.data;
  }

  /** The authoritative gate — the figures returned include this call. */
  async recordUsage(request: RecordUsageRequest): Promise<RecordUsageResult> {
    const response = await serviceInstances.utitlitiesService.post<
      SimulationApiResponse<RecordUsageResult>
    >(SUBSCRIPTION_USAGE_ENDPOINT, request);

    if (!response.success || !response.data) {
      throw new Error(response.error?.message || "The usage could not be recorded.");
    }

    return response.data;
  }
}

export const subscriptionSimulationService = new SubscriptionSimulationService();
