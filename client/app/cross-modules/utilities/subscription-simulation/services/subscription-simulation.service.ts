import { HttpError } from "@seliseblocks/genesis-os/lib";
import { serviceInstances } from "@/lib/http-client";
import {
  AUDIT_TRAIL_DEFAULT_LIMIT,
  ENTITLEMENTS_ENDPOINT,
  SUBSCRIPTION_USAGE_ENDPOINT,
  SUBSCRIPTIONS_CURRENT_ENDPOINT,
  SUBSCRIPTIONS_ENDPOINT,
} from "../constants/subscription-simulation.constants";
import { subscriptionApiFailure } from "../../subscription/utilities/subscription-api-failure";
import type {
  CancelSubscriptionRequest,
  ChangeQuantityRequest,
  ChangeSubscriptionPlanRequest,
  QuantityChangeQuote,
  EntitlementDecision,
  EntitlementsSnapshot,
  RecordUsageRequest,
  RecordUsageResult,
  SimulatedSubscription,
  SubscribeToPlanRequest,
  SubscriptionAuditEvent,
  SubscriptionPurchasePreview,
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

/**
 * A failed call, with the server's own error code kept.
 *
 * Thrown rather than a bare Error for the quantity paths, where four outcomes have to be told
 * apart and only the code distinguishes them: a declined card, a charge whose outcome is unknown,
 * a stale version, and another settlement already in flight. Reduced to a message, "the change
 * could not be applied" is the same sentence for a retry that is safe and one that is not.
 */
export class SubscriptionOperationError extends Error {
  constructor(
    message: string,
    readonly code: string,
    readonly status?: number,
    /**
     * The server's field map, where it sent one.
     *
     * Carried because some refusals are answerable: an incomplete billing profile names exactly
     * which details are still needed, and dropping them leaves the screen able only to say no.
     */
    readonly fields: Record<string, string[]> = {},
  ) {
    super(message);
    this.name = "SubscriptionOperationError";
  }
}

/**
 * Turns whatever a money-moving call threw into the server's own refusal.
 *
 * The envelope arrives inside an `HttpError` for any failing status and inline in the body for a
 * `success: false` with a 200, so both are read the same way here rather than at each call site.
 * `fallback` is used only when there is no envelope to read — a network fault, or a shape nobody
 * expected.
 *
 * Reads the envelope rather than searching for a known code in the serialized error, which is what
 * {@link subscribeErrorCode} does for the preview path. Both work; this one also keeps `fields`,
 * which is what lets a screen name the details a profile is still missing. Worth collapsing onto
 * one of the two, but not in the middle of a merge.
 */
const operationError = (error: unknown, fallback: string): SubscriptionOperationError => {
  if (error instanceof SubscriptionOperationError) {
    return error;
  }

  const failure = subscriptionApiFailure(error);
  const status = error instanceof HttpError ? error.status : undefined;

  return new SubscriptionOperationError(
    failure?.message || (error instanceof Error && error.message) || fallback,
    failure?.code ?? "unknown",
    status,
    failure?.fields ?? {},
  );
};

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

  /**
   * What subscribing would cost right now, and what would stand in the way, without starting
   * anything.
   *
   * A blocker in the response — an existing subscription, an incomplete billing profile — is not
   * an error: the price is returned alongside it, because the point of a preview is to show both
   * together. Only a genuine input problem (an unknown plan, price or discount code) throws here,
   * with the same code {@link subscribe} would then fail with.
   */
  async previewSubscription(
    request: SubscribeToPlanRequest,
  ): Promise<SubscriptionPurchasePreview> {
    try {
      const response = await serviceInstances.utitlitiesService.post<
        SimulationApiResponse<SubscriptionPurchasePreview>
      >(`${SUBSCRIPTIONS_ENDPOINT}/preview`, request);

      if (!response.success || !response.data) {
        throw new SubscriptionOperationError(
          response.error?.message || "The subscription could not be previewed.",
          response.error?.code ?? "unknown",
        );
      }

      return response.data;
    } catch (error) {
      if (error instanceof SubscriptionOperationError) {
        throw error;
      }

      if (error instanceof HttpError) {
        throw new SubscriptionOperationError(
          messageFrom(error, "The subscription could not be previewed."),
          subscribeErrorCode(error),
          error.status,
        );
      }

      throw error;
    }
  }

  /**
   * Starts a subscription, keeping the server's refusal code and fields intact.
   *
   * The refusals here are answerable ones — an incomplete billing profile, an unknown discount
   * code — and a screen can only offer the fix if it still knows which refusal it was.
   */
  async subscribe(request: SubscribeToPlanRequest): Promise<SimulatedSubscription> {
    try {
      const response = await serviceInstances.utitlitiesService.post<
        SimulationApiResponse<SimulatedSubscription>
      >(SUBSCRIPTIONS_ENDPOINT, request);

      if (!response.success || !response.data) {
        throw operationError(response, "The subscription could not be started.");
      }

      return response.data;
    } catch (error) {
      throw operationError(error, "The subscription could not be started.");
    }
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
   * Moves an existing subscription to another price. The whole request is body-bound
   * (`[FromBody]` on the server), so — unlike the GET/DELETE endpoints — `organizationId` has to
   * travel as a field on `request`, not as a query parameter.
   */
  async changePlan(
    subscriptionId: string,
    request: ChangeSubscriptionPlanRequest,
  ): Promise<SimulatedSubscription> {
    try {
      const response = await serviceInstances.utitlitiesService.put<
        SimulationApiResponse<SimulatedSubscription>
      >(`${SUBSCRIPTIONS_ENDPOINT}/${encodeURIComponent(subscriptionId)}/plan`, request);

      if (!response.success || !response.data) {
        throw operationError(response, "The plan could not be changed.");
      }

      return response.data;
    } catch (error) {
      // Changing a plan is a money-moving call and refuses for the same billing-profile reason a
      // new subscription does, so it keeps the code as well.
      throw operationError(error, "The plan could not be changed.");
    }
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

  /**
   * The immutable lifecycle trail for one subscription, newest first. Read-only and
   * subscription-specific — there is no tenant-wide audit search, and the response never carries
   * an actor id or a payment id (see the doc's "financial operation tracing and audit" section).
   */
  async getAuditTrail(
    subscriptionId: string,
    organizationId?: string,
    limit: number = AUDIT_TRAIL_DEFAULT_LIMIT,
  ): Promise<SubscriptionAuditEvent[]> {
    const query = new URLSearchParams();
    query.set("limit", String(limit));
    if (organizationId) {
      query.set("organizationId", organizationId);
    }

    const response = await serviceInstances.utitlitiesService.get<
      SimulationApiResponse<SubscriptionAuditEvent[]>
    >(`${SUBSCRIPTIONS_ENDPOINT}/${encodeURIComponent(subscriptionId)}/audit?${query}`);

    if (!response.success || !response.data) {
      throw new Error(response.error?.message || "The audit trail could not be loaded.");
    }

    return response.data;
  }

  /**
   * What a quantity change would cost, writing nothing.
   *
   * A separate endpoint rather than a flag on the update: the preview and the apply share a request
   * body, so what a subscriber is quoted is what the confirm then sends.
   */
  async previewQuantityChange(
    subscriptionId: string,
    request: ChangeQuantityRequest,
  ): Promise<QuantityChangeQuote> {
    return this.quantityCall("post", `${this.quantityPath(subscriptionId)}/preview`, request);
  }

  /** Applies the change the preview quoted. An increase is charged before the units move. */
  async changeQuantity(
    subscriptionId: string,
    request: ChangeQuantityRequest,
  ): Promise<QuantityChangeQuote> {
    return this.quantityCall("put", this.quantityPath(subscriptionId), request);
  }

  /**
   * Withdraws a scheduled decrease, leaving the current quantity in place.
   *
   * Scope travels in the query here rather than a body, because that is what this endpoint reads —
   * a DELETE with no body to put it in.
   */
  async cancelPendingQuantityChange(
    subscriptionId: string,
    organizationId?: string,
  ): Promise<QuantityChangeQuote> {
    const query = organizationId
      ? `?organizationId=${encodeURIComponent(organizationId)}`
      : "";

    return this.quantityCall(
      "delete",
      `${this.quantityPath(subscriptionId)}/pending${query}`,
    );
  }

  private quantityPath(subscriptionId: string): string {
    return `${SUBSCRIPTIONS_ENDPOINT}/${encodeURIComponent(subscriptionId)}/quantities`;
  }

  private async quantityCall(
    method: "post" | "put" | "delete",
    path: string,
    request?: ChangeQuantityRequest,
  ): Promise<QuantityChangeQuote> {
    try {
      const client = serviceInstances.utitlitiesService;
      const response =
        method === "delete"
          ? await client.delete<SimulationApiResponse<QuantityChangeQuote>>(path)
          : method === "put"
            ? await client.put<SimulationApiResponse<QuantityChangeQuote>>(path, request)
            : await client.post<SimulationApiResponse<QuantityChangeQuote>>(path, request);

      if (!response.success || !response.data) {
        throw new SubscriptionOperationError(
          response.error?.message || "The quantity could not be changed.",
          response.error?.code ?? "unknown",
        );
      }

      return response.data;
    } catch (error) {
      if (error instanceof SubscriptionOperationError) {
        throw error;
      }

      // A 409 or 422 arrives as an HttpError, and where the envelope's error lands on it varies
      // with the transport — HttpError carries `errors`, not the response body. Searched for
      // rather than read from a fixed path, the same way the payment module recognizes its own
      // provider-unavailable code, because guessing one path and being wrong turns every named
      // outcome into "unknown" and every retry into a guess.
      if (error instanceof HttpError) {
        throw new SubscriptionOperationError(
          messageFrom(error, "The quantity could not be changed."),
          quantityErrorCode(error),
          error.status,
        );
      }

      throw error;
    }
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

/**
 * The outcomes a caller has to tell apart, and nothing else.
 *
 * Listed rather than parsed out of a path on the error object: the envelope arrives in different
 * shapes for a 409 and a 422, and a code read from the wrong place is silently "unknown" — which
 * is the one answer that makes a charge-unresolved look like a decline.
 */
const QUANTITY_ERROR_CODES = [
  "subscription_quantity_charge_unresolved",
  "subscription_quantity_charge_failed",
  "subscription_quantity_change_in_flight",
  "subscription_version_conflict",
  "subscription_payment_method_missing",
  "subscription_quantity_change_not_allowed",
  "subscription_quantity_item_unknown",
  "subscription_quantity_unchanged",
  "subscription_quantity_invalid",
  "subscription_pending_quantity_change_not_found",
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

/**
 * The outcomes {@link SubscriptionSimulationService.subscribe} and
 * {@link SubscriptionSimulationService.previewSubscription} both refuse for — genuine input
 * problems, as opposed to the billing-profile and already-active conditions a preview reports as
 * a blocker rather than an error.
 */
const SUBSCRIBE_ERROR_CODES = [
  "subscription_plan_not_found",
  "subscription_price_not_found",
  "subscription_quantity_invalid",
  "subscription_schedule_invalid",
  "subscription_discount_not_found",
  "subscription_discount_expired",
  "subscription_discount_not_applicable",
  "subscription_discount_currency_mismatch",
  "subscription_request_invalid",
] as const;

const codeFrom = (
  error: unknown,
  candidates: readonly string[],
): string => {
  const haystack = `${serialize(error)} ${error instanceof Error ? error.message : ""}`;

  return candidates.find((code) => haystack.includes(code)) ?? "unknown";
};

const quantityErrorCode = (error: unknown): string => codeFrom(error, QUANTITY_ERROR_CODES);

const subscribeErrorCode = (error: unknown): string => codeFrom(error, SUBSCRIBE_ERROR_CODES);

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

export const subscriptionSimulationService = new SubscriptionSimulationService();
