import { HttpError } from "@seliseblocks/genesis-os/lib";
import { serviceInstances } from "@/lib/http-client";
import { SUBSCRIPTIONS_ENDPOINT } from "../constants/subscription-simulation.constants";
import { subscriptionApiFailure } from "../../subscription/utilities/subscription-api-failure";
import type {
  CreditGrant,
  CreditMove,
  CreditPurchase,
  CreditPurchaseRequest,
  CreditQuote,
  GrantCreditsRequest,
  MoveCreditsRequest,
} from "../models/subscription-simulation.model";
import { SubscriptionOperationError } from "./subscription-simulation.service";

interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  error: { code: string; message: string } | null;
}

/**
 * The credit endpoints under one subscription. See spec 002.
 *
 * Every failure is a {@link SubscriptionOperationError} carrying the server's code, because the
 * dialogs tell a declined card from a trial or a scheduled cancellation by it, and each asks the
 * tester to do something different.
 */
const call = async <T>(
  request: () => Promise<ApiResponse<T>>,
  fallback: string,
): Promise<T> => {
  try {
    const response = await request();

    if (!response.success || response.data === null) {
      throw new SubscriptionOperationError(
        response.error?.message || fallback,
        response.error?.code ?? "unknown",
      );
    }

    return response.data;
  } catch (error) {
    if (error instanceof SubscriptionOperationError) {
      throw error;
    }

    const failure = subscriptionApiFailure(error);
    throw new SubscriptionOperationError(
      failure?.message || (error instanceof Error && error.message) || fallback,
      failure?.code ?? "unknown",
      error instanceof HttpError ? error.status : undefined,
      failure?.fields ?? {},
    );
  }
};

const creditsPath = (subscriptionId: string) =>
  `${SUBSCRIPTIONS_ENDPOINT}/${encodeURIComponent(subscriptionId)}/credits`;

const scopeQuery = (organizationId?: string) =>
  organizationId ? `?organizationId=${encodeURIComponent(organizationId)}` : "";

export const subscriptionCreditsService = {
  previewPurchase: (subscriptionId: string, request: CreditPurchaseRequest) =>
    call(
      () =>
        serviceInstances.utitlitiesService.post<ApiResponse<CreditQuote>>(
          `${creditsPath(subscriptionId)}/preview`,
          request,
        ),
      "The credits could not be quoted.",
    ),

  purchase: (subscriptionId: string, request: CreditPurchaseRequest) =>
    call(
      () =>
        serviceInstances.utitlitiesService.post<ApiResponse<CreditPurchase>>(
          creditsPath(subscriptionId),
          request,
        ),
      "The credits could not be bought.",
    ),

  listGrants: (subscriptionId: string, organizationId?: string) =>
    call(
      () =>
        serviceInstances.utitlitiesService.get<ApiResponse<CreditGrant[]>>(
          `${creditsPath(subscriptionId)}${scopeQuery(organizationId)}`,
        ),
      "The credits could not be loaded.",
    ),

  grant: (subscriptionId: string, request: GrantCreditsRequest) =>
    call(
      () =>
        serviceInstances.utitlitiesService.post<ApiResponse<CreditGrant>>(
          `${creditsPath(subscriptionId)}/grants`,
          request,
        ),
      "The credits could not be granted.",
    ),

  move: (subscriptionId: string, request: MoveCreditsRequest) =>
    call(
      () =>
        serviceInstances.utitlitiesService.post<ApiResponse<CreditMove>>(
          `${creditsPath(subscriptionId)}/moves`,
          request,
        ),
      "The credits could not be moved.",
    ),
};
