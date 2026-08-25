import { serviceInstances } from "@/lib/http-client";
import {
  SUBSCRIPTION_BILLING_PROFILE_ENDPOINT,
  SUBSCRIPTION_INVOICES_ENDPOINT,
} from "../constants/subscription.constants";
import type {
  FinancialDocumentQuery,
  SubscriptionBillingProfile,
  SubscriptionFinancialDocumentPage,
  UpdateBillingProfileRequest,
} from "../models/subscription-billing.model";

interface SubscriptionApiError {
  code: string;
  message: string;
  fields?: Record<string, string[]> | null;
}

interface SubscriptionApiResponse<T> {
  success: boolean;
  data: T | null;
  error: SubscriptionApiError | null;
}

const organizationQuery = (organizationId?: string): string =>
  organizationId ? `?organizationId=${encodeURIComponent(organizationId)}` : "";

/**
 * Turns a document query into a query string, omitting anything unset.
 *
 * Omitted rather than sent empty, because the server reads an absent filter as "all of them" and an
 * empty one as a value it has to validate — and a blank `documentType` would be refused rather than
 * ignored.
 */
const documentQuery = (query: FinancialDocumentQuery): string => {
  const params = new URLSearchParams();

  if (query.pageSize) params.set("pageSize", String(query.pageSize));
  if (query.after) params.set("after", query.after);
  if (query.subscriptionId) params.set("subscriptionId", query.subscriptionId);
  if (query.documentType) params.set("documentType", query.documentType);
  if (query.status) params.set("status", query.status);
  if (query.issuedFromUtc) params.set("issuedFromUtc", query.issuedFromUtc);
  if (query.issuedToUtc) params.set("issuedToUtc", query.issuedToUtc);
  if (query.organizationId) params.set("organizationId", query.organizationId);

  const serialized = params.toString();

  return serialized ? `?${serialized}` : "";
};

class SubscriptionBillingService {
  async getBillingProfile(
    organizationId?: string,
  ): Promise<SubscriptionBillingProfile> {
    const response = await serviceInstances.utitlitiesService.get<
      SubscriptionApiResponse<SubscriptionBillingProfile>
    >(`${SUBSCRIPTION_BILLING_PROFILE_ENDPOINT}${organizationQuery(organizationId)}`);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || "The billing profile could not be loaded.",
      );
    }

    return response.data;
  }

  async updateBillingProfile(
    request: UpdateBillingProfileRequest,
  ): Promise<SubscriptionBillingProfile> {
    const response = await serviceInstances.utitlitiesService.put<
      SubscriptionApiResponse<SubscriptionBillingProfile>
    >(SUBSCRIPTION_BILLING_PROFILE_ENDPOINT, request);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || "The billing profile could not be saved.",
      );
    }

    return response.data;
  }

  async listDocuments(
    query: FinancialDocumentQuery = {},
  ): Promise<SubscriptionFinancialDocumentPage> {
    const response = await serviceInstances.utitlitiesService.get<
      SubscriptionApiResponse<SubscriptionFinancialDocumentPage>
    >(`${SUBSCRIPTION_INVOICES_ENDPOINT}${documentQuery(query)}`);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || "The invoices could not be loaded.",
      );
    }

    return response.data;
  }

  /**
   * Fetches one document's PDF as a blob.
   *
   * Through the authenticated client rather than by pointing the browser at the URL: the endpoint is
   * behind the caller's own authorization, and a plain link would arrive without it. The client
   * returns a blob for `application/pdf`, so nothing here has to parse anything.
   */
  async downloadDocumentPdf(
    documentId: string,
    organizationId?: string,
  ): Promise<Blob> {
    return await serviceInstances.utitlitiesService.get<Blob>(
      `${SUBSCRIPTION_INVOICES_ENDPOINT}/${encodeURIComponent(documentId)}/pdf` +
        organizationQuery(organizationId),
    );
  }
}

export const subscriptionBillingService = new SubscriptionBillingService();
