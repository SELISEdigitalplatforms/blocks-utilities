import { HttpError } from "@seliseblocks/genesis-os/lib";
import { serviceInstances } from "@/lib/http-client";
import { tStatic } from "@seliseblocks/genesis-os";
import { subscriptionApiFailure } from "../../subscription/utilities/subscription-api-failure";
import type { SubscriptionFinancialDocumentPage } from "../../subscription/models/subscription-billing.model";
import type {
  CreditGrant,
  CreditPurchase,
  CreditQuote,
  OrganizationCreditPurchaseRequest,
  OrganizationGrantCreditsRequest,
} from "../models/subscription-simulation.model";
import { SubscriptionOperationError } from "./subscription-simulation.service";

const MODULE = "utilities.subscription-simulation";

export const ORGANIZATION_CREDITS_ENDPOINT = "/api/organization-credits";

interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  error: { code: string; message: string } | null;
}

const call = async <T>(request: () => Promise<ApiResponse<T>>, fallback: string): Promise<T> => {
  try {
    const response = await request();

    if (!response.success || response.data === null) {
      throw new SubscriptionOperationError(response.error?.message || fallback, response.error?.code ?? "unknown");
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

const query = (parameters: Record<string, string | undefined>) => {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(parameters)) {
    if (value) search.set(key, value);
  }
  return search.size > 0 ? `?${search.toString()}` : "";
};

/** The standalone credits an organization holds outside any subscription. See spec 005. */
export const organizationCreditsService = {
  previewPurchase: (request: OrganizationCreditPurchaseRequest) =>
    call(
      () =>
        serviceInstances.utitlitiesService.post<ApiResponse<CreditQuote>>(
          `${ORGANIZATION_CREDITS_ENDPOINT}/preview`,
          request,
        ),
      tStatic(MODULE, "ORGANIZATION_CREDITS_SERVICE.QUOTE_FAILED"),
    ),

  purchase: (request: OrganizationCreditPurchaseRequest) =>
    call(
      () =>
        serviceInstances.utitlitiesService.post<ApiResponse<CreditPurchase>>(
          ORGANIZATION_CREDITS_ENDPOINT,
          request,
        ),
      tStatic(MODULE, "ORGANIZATION_CREDITS_SERVICE.PURCHASE_FAILED"),
    ),

  listGrants: (organizationId?: string) =>
    call(
      () =>
        serviceInstances.utitlitiesService.get<ApiResponse<CreditGrant[]>>(
          `${ORGANIZATION_CREDITS_ENDPOINT}${query({ organizationId })}`,
        ),
      tStatic(MODULE, "ORGANIZATION_CREDITS_SERVICE.GRANTS_FAILED"),
    ),

  grant: (request: OrganizationGrantCreditsRequest) =>
    call(
      () =>
        serviceInstances.utitlitiesService.post<ApiResponse<CreditGrant>>(
          `${ORGANIZATION_CREDITS_ENDPOINT}/grants`,
          request,
        ),
      tStatic(MODULE, "ORGANIZATION_CREDITS_SERVICE.GRANT_FAILED"),
    ),

  listPurchases: (organizationId?: string) =>
    call(
      () =>
        serviceInstances.utitlitiesService.get<ApiResponse<CreditPurchase[]>>(
          `${ORGANIZATION_CREDITS_ENDPOINT}/purchases${query({ organizationId })}`,
        ),
      tStatic(MODULE, "ORGANIZATION_CREDITS_SERVICE.PURCHASES_FAILED"),
    ),

  listInvoices: (organizationId?: string, after?: string | null) =>
    call(
      () =>
        serviceInstances.utitlitiesService.get<ApiResponse<SubscriptionFinancialDocumentPage>>(
          `${ORGANIZATION_CREDITS_ENDPOINT}/invoices${query({ organizationId, pageSize: "25", after: after ?? undefined })}`,
        ),
      tStatic(MODULE, "ORGANIZATION_CREDITS_SERVICE.INVOICES_FAILED"),
    ),
};
