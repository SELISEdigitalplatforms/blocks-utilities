import { serviceInstances } from "@/lib/http-client";
import {
  SUBSCRIPTION_BILLING_PROFILE_ENDPOINT,
  SUBSCRIPTION_INVOICES_ENDPOINT,
  SUBSCRIPTION_MERCHANT_PROFILE_ENDPOINT,
} from "../constants/subscription.constants";
import type {
  FinancialDocumentQuery,
  ResendFinancialDocumentResponse,
  SubscriptionBillingProfile,
  SubscriptionFinancialDocumentPage,
  SubscriptionMerchantProfile,
  UpdateBillingProfileRequest,
  UpdateMerchantProfileRequest,
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

  /**
   * Reads the tenant's own selling identity.
   *
   * No organization parameter, because the seller is the tenant. Passing one would suggest an
   * organization could have a seller of its own, which is exactly the confusion this is scoped to
   * avoid.
   */
  async getMerchantProfile(): Promise<SubscriptionMerchantProfile> {
    const response = await serviceInstances.utitlitiesService.get<
      SubscriptionApiResponse<SubscriptionMerchantProfile>
    >(SUBSCRIPTION_MERCHANT_PROFILE_ENDPOINT);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || "The merchant profile could not be loaded.",
      );
    }

    return response.data;
  }

  async updateMerchantProfile(
    request: UpdateMerchantProfileRequest,
  ): Promise<SubscriptionMerchantProfile> {
    const response = await serviceInstances.utitlitiesService.put<
      SubscriptionApiResponse<SubscriptionMerchantProfile>
    >(SUBSCRIPTION_MERCHANT_PROFILE_ENDPOINT, request);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || "The merchant profile could not be saved.",
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

  /**
   * Queues a document for another delivery attempt. Console only — the server refuses anybody else.
   */
  async resendDocument(documentId: string): Promise<ResendFinancialDocumentResponse> {
    const response = await serviceInstances.utitlitiesService.post<
      SubscriptionApiResponse<ResendFinancialDocumentResponse>
    >(`${SUBSCRIPTION_INVOICES_ENDPOINT}/${encodeURIComponent(documentId)}/resend`, {});

    if (!response.success || !response.data) {
      throw new Error(response.error?.message || "The document could not be resent.");
    }

    return response.data;
  }
}

export const subscriptionBillingService = new SubscriptionBillingService();
