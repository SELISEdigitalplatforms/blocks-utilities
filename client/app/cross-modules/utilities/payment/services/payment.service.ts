import { serviceInstances } from "@/lib/http-client";
import {
  CREATE_PAYMENT_ENDPOINT,
  PAYMENT_ENDPOINT,
  PAYMENT_PROVIDERS_ENDPOINT,
  STORED_PAYMENT_METHODS_ENDPOINT,
} from "../constants/payment.constants";
import type {
  CreatedPayment,
  CreatePaymentCommand,
} from "../models/create-payment.model";
import type {
  PaymentApiResponse,
  PaymentListData,
  PaymentQuery,
} from "../models/payment.model";
import type {
  CreatePaymentRefundCommand,
  PaymentRefund,
} from "../models/payment-refund.model";
import type {
  StoredPaymentMethod,
  StoredPaymentMethodRemovalOutcome,
  StoredPaymentMethodRemovalResponse,
} from "../models/stored-payment-method.model";
import type {
  PaymentProvider,
  RegisteredPaymentProvider,
  RegisterPaymentProviderRequest,
  RotatePaymentProviderCredentialsCommand,
  UpdatePaymentProviderCommand,
} from "../models/payment-provider.model";

const toUtcDayStart = (value: string): string =>
  new Date(`${value}T00:00:00.000Z`).toISOString();

const toExclusiveUtcDayEnd = (value: string): string => {
  const date = new Date(`${value}T00:00:00.000Z`);
  date.setUTCDate(date.getUTCDate() + 1);
  return date.toISOString();
};

const appendIfPresent = (
  parameters: URLSearchParams,
  key: string,
  value: string,
) => {
  const normalized = value.trim();

  if (normalized) {
    parameters.append(key, normalized);
  }
};

export const createPaymentQueryParameters = (
  query: PaymentQuery,
): URLSearchParams => {
  const parameters = new URLSearchParams({
    pageSize: query.pageSize.toString(),
    sortBy: query.sortBy,
    sortDirection: query.sortDirection,
  });

  query.filters.providerNames.forEach((providerName) =>
    parameters.append("providerNames", providerName),
  );
  query.filters.paymentStatuses.forEach((paymentStatus) =>
    parameters.append("paymentStatuses", paymentStatus),
  );

  appendIfPresent(parameters, "minAmount", query.filters.minAmount);
  appendIfPresent(parameters, "maxAmount", query.filters.maxAmount);
  appendIfPresent(
    parameters,
    "currencyCode",
    query.filters.currencyCode.toUpperCase(),
  );
  appendIfPresent(parameters, "orderId", query.filters.orderId);
  appendIfPresent(
    parameters,
    "paymentDetailId",
    query.filters.paymentDetailId,
  );
  appendIfPresent(parameters, "paymentFlow", query.filters.paymentFlow);

  if (query.filters.paymentDateFrom) {
    parameters.append(
      "paymentDateFromUtc",
      toUtcDayStart(query.filters.paymentDateFrom),
    );
  }

  if (query.filters.paymentDateTo) {
    parameters.append(
      "paymentDateToUtc",
      toExclusiveUtcDayEnd(query.filters.paymentDateTo),
    );
  }

  if (query.after) {
    parameters.append("after", query.after);
  }

  if (query.before) {
    parameters.append("before", query.before);
  }

  return parameters;
};

class PaymentService {
  async getPaymentProviders(): Promise<PaymentProvider[]> {
    const response =
      await serviceInstances.utitlitiesService.get<
        PaymentApiResponse<PaymentProvider[]>
      >(PAYMENT_PROVIDERS_ENDPOINT);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message ||
          "Payment providers could not be loaded.",
      );
    }

    return response.data;
  }

  async registerPaymentProvider(
    request: RegisterPaymentProviderRequest,
  ): Promise<RegisteredPaymentProvider> {
    const response =
      await serviceInstances.utitlitiesService.post<
        PaymentApiResponse<RegisteredPaymentProvider>
      >(PAYMENT_PROVIDERS_ENDPOINT, request);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message ||
          "The payment provider could not be created.",
      );
    }

    return response.data;
  }

  async updatePaymentProvider({
    paymentProviderId,
    request,
  }: UpdatePaymentProviderCommand): Promise<PaymentProvider> {
    const response =
      await serviceInstances.utitlitiesService.put<
        PaymentApiResponse<PaymentProvider>
      >(
        `${PAYMENT_PROVIDERS_ENDPOINT}/${encodeURIComponent(paymentProviderId)}`,
        request,
      );

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message ||
          "The payment provider could not be updated.",
      );
    }

    return response.data;
  }

  async rotatePaymentProviderCredentials({
    paymentProviderId,
    request,
  }: RotatePaymentProviderCredentialsCommand): Promise<PaymentProvider> {
    const response =
      await serviceInstances.utitlitiesService.post<
        PaymentApiResponse<PaymentProvider>
      >(
        `${PAYMENT_PROVIDERS_ENDPOINT}/${encodeURIComponent(paymentProviderId)}/rotate`,
        request,
      );

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message ||
          "The provider credentials could not be rotated.",
      );
    }

    return response.data;
  }

  async getStoredPaymentMethods(): Promise<StoredPaymentMethod[]> {
    const response =
      await serviceInstances.utitlitiesService.get<
        PaymentApiResponse<StoredPaymentMethod[]>
      >(STORED_PAYMENT_METHODS_ENDPOINT);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message ||
          "Saved payment methods could not be loaded.",
      );
    }

    return response.data;
  }

  async removeStoredPaymentMethod(
    paymentMethodId: string,
  ): Promise<StoredPaymentMethodRemovalOutcome> {
    const response =
      await serviceInstances.utitlitiesService.delete<
        | PaymentApiResponse<StoredPaymentMethodRemovalResponse>
        | undefined
      >(
        `${STORED_PAYMENT_METHODS_ENDPOINT}/${encodeURIComponent(paymentMethodId)}`,
      );

    return response?.data?.status === "REMOVAL_PENDING"
      ? "pending"
      : "removed";
  }

  async createPayment({
    request,
    idempotencyKey,
  }: CreatePaymentCommand): Promise<CreatedPayment> {
    const response =
      await serviceInstances.utitlitiesService.post<
        PaymentApiResponse<CreatedPayment>
      >(
        CREATE_PAYMENT_ENDPOINT,
        request,
        { "Idempotency-Key": idempotencyKey },
      );

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || "The payment could not be created.",
      );
    }

    return response.data;
  }

  async createPaymentRefund({
    paymentDetailId,
    request,
    idempotencyKey,
  }: CreatePaymentRefundCommand): Promise<PaymentRefund> {
    const response =
      await serviceInstances.utitlitiesService.post<
        PaymentApiResponse<PaymentRefund>
      >(
        `${PAYMENT_ENDPOINT}/${encodeURIComponent(paymentDetailId)}/refunds`,
        request,
        { "Idempotency-Key": idempotencyKey },
      );

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message ||
          "The refund request could not be submitted.",
      );
    }

    return response.data;
  }

  async getPayments(query: PaymentQuery): Promise<PaymentListData> {
    const parameters = createPaymentQueryParameters(query);
    const response =
      await serviceInstances.utitlitiesService.get<
        PaymentApiResponse<PaymentListData>
      >(`${PAYMENT_ENDPOINT}?${parameters.toString()}`);

    if (!response.success || !response.data) {
      throw new Error(
        response.error?.message || "Payments could not be loaded.",
      );
    }

    return response.data;
  }
}

export const paymentService = new PaymentService();
