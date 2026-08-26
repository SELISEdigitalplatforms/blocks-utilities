import { describe, expect, it, vi } from "vitest";
import type { PaymentQuery } from "../models/payment.model";

vi.mock("@/lib/http-client", () => ({
  serviceInstances: {
    utitlitiesService: {
      delete: vi.fn(),
      get: vi.fn(),
      post: vi.fn(),
      put: vi.fn(),
    },
  },
}));

import { serviceInstances } from "@/lib/http-client";
import {
  createPaymentQueryParameters,
  paymentService,
} from "./payment.service";

const createQuery = (
  overrides: Partial<PaymentQuery> = {},
): PaymentQuery => ({
  pageSize: 25,
  sortBy: "paymentDate",
  sortDirection: "desc",
  filters: {
    providerNames: [],
    paymentStatuses: [],
    minAmount: "",
    maxAmount: "",
    paymentDateFrom: "",
    paymentDateTo: "",
    currencyCode: "",
    orderId: "",
    paymentDetailId: "",
    paymentFlow: "",
    organizationId: "",
  },
  ...overrides,
});

describe("createPaymentQueryParameters", () => {
  it("serializes repeated filters and exact values", () => {
    const query = createQuery({
      filters: {
        providerNames: ["ADYEN-ONLINE", "STRIPE"],
        paymentStatuses: ["AUTHORIZED", "CAPTURED"],
        minAmount: "10",
        maxAmount: "500",
        paymentDateFrom: "",
        paymentDateTo: "",
        currencyCode: "chf",
        orderId: "ORDER-1001",
        paymentDetailId: "payment-1",
        paymentFlow: "HOSTED_CHECKOUT",
        organizationId: "organization-2",
      },
    });

    const parameters = createPaymentQueryParameters(query);

    expect(parameters.getAll("providerNames")).toEqual([
      "ADYEN-ONLINE",
      "STRIPE",
    ]);
    expect(parameters.getAll("paymentStatuses")).toEqual([
      "AUTHORIZED",
      "CAPTURED",
    ]);
    expect(parameters.get("currencyCode")).toBe("CHF");
    expect(parameters.get("minAmount")).toBe("10");
    expect(parameters.get("maxAmount")).toBe("500");
    expect(parameters.get("orderId")).toBe("ORDER-1001");
    expect(parameters.get("paymentDetailId")).toBe("payment-1");
    expect(parameters.get("paymentFlow")).toBe("HOSTED_CHECKOUT");
    expect(parameters.get("organizationId")).toBe("organization-2");
  });

  it("drops a filter whose value is missing rather than throwing", () => {
    // Filter state can outlive the shape it was saved under: a value persisted before a
    // filter existed arrives here undefined, and that must drop one parameter rather than
    // throw and take the whole payment list down.
    const query = createQuery();
    (query.filters as Record<string, unknown>).organizationId = undefined;

    const parameters = createPaymentQueryParameters(query);

    expect(parameters.has("organizationId")).toBe(false);
  });

  it("converts an inclusive date selection into UTC API boundaries", () => {
    const query = createQuery({
      filters: {
        ...createQuery().filters,
        paymentDateFrom: "2026-07-01",
        paymentDateTo: "2026-07-31",
      },
    });

    const parameters = createPaymentQueryParameters(query);

    expect(parameters.get("paymentDateFromUtc")).toBe(
      "2026-07-01T00:00:00.000Z",
    );
    expect(parameters.get("paymentDateToUtc")).toBe(
      "2026-08-01T00:00:00.000Z",
    );
  });

  it("sends only the active cursor direction", () => {
    const nextPage = createPaymentQueryParameters(
      createQuery({ after: "next-cursor" }),
    );
    const previousPage = createPaymentQueryParameters(
      createQuery({ before: "previous-cursor" }),
    );

    expect(nextPage.get("after")).toBe("next-cursor");
    expect(nextPage.has("before")).toBe(false);
    expect(previousPage.get("before")).toBe("previous-cursor");
    expect(previousPage.has("after")).toBe(false);
  });
});

describe("paymentService.createPayment", () => {
  it("sends the payment payload with its idempotency key", async () => {
    const response = {
      success: true,
      data: {
        paymentDetailId: "payment-1",
        providerName: "ADYEN-ONLINE",
        paymentStatus: "PROCESSING",
        orderId: "ORDER-1001",
        amount: 10,
        currencyCode: "CHF",
        redirectUrl: "https://test.adyen.link/session",
        expiresAtUtc: "2026-07-23T12:00:00Z",
      },
      error: null,
      meta: {
        correlationId: "correlation-1",
        timestampUtc: "2026-07-23T11:00:00Z",
      },
    };
    vi.mocked(
      serviceInstances.utitlitiesService.post,
    ).mockResolvedValue(response);

    const result = await paymentService.createPayment({
      request: {
        providerName: "ADYEN-ONLINE",
        amount: 10,
        currencyCode: "CHF",
        orderId: "ORDER-1001",
        rememberCard: false,
        isRecurring: false,
      },
      idempotencyKey: "550e8400-e29b-41d4-a716-446655440049",
    });

    expect(
      serviceInstances.utitlitiesService.post,
    ).toHaveBeenCalledWith(
      "/api/payments/create",
      {
        providerName: "ADYEN-ONLINE",
        amount: 10,
        currencyCode: "CHF",
        orderId: "ORDER-1001",
        rememberCard: false,
        isRecurring: false,
      },
      {
        "Idempotency-Key":
          "550e8400-e29b-41d4-a716-446655440049",
      },
    );
    expect(result).toEqual(response.data);
  });

  it("returns the safe API error message when creation fails", async () => {
    vi.mocked(
      serviceInstances.utitlitiesService.post,
    ).mockResolvedValue({
      success: false,
      data: null,
      error: {
        code: "invalid_payment",
        message: "The payment request is invalid.",
      },
      meta: {
        correlationId: "correlation-1",
        timestampUtc: "2026-07-23T11:00:00Z",
      },
    });

    await expect(
      paymentService.createPayment({
        request: {
          providerName: "ADYEN-ONLINE",
          amount: 10,
          currencyCode: "CHF",
          orderId: "ORDER-1001",
          rememberCard: false,
          isRecurring: false,
        },
        idempotencyKey:
          "550e8400-e29b-41d4-a716-446655440049",
      }),
    ).rejects.toThrow("The payment request is invalid.");
  });
});

describe("paymentService.createPaymentRefund", () => {
  it("sends the refund request with its idempotency key", async () => {
    const response = {
      success: true,
      data: {
        refundId: "refund-1",
        paymentDetailId: "payment-1",
        status: "SUBMITTED",
        amount: 4,
        currencyCode: "CHF",
        operation: "REFUND",
        completionAction: null,
        failureCode: null,
        failureSummary: null,
        createdAtUtc: "2026-07-23T11:00:00Z",
        completedAtUtc: null,
      },
      error: null,
      meta: {
        correlationId: "correlation-1",
        timestampUtc: "2026-07-23T11:00:00Z",
      },
    };
    vi.mocked(
      serviceInstances.utitlitiesService.post,
    ).mockResolvedValue(response);

    const result = await paymentService.createPaymentRefund({
      paymentDetailId: "payment/1",
      request: {
        amount: 4,
        reason: "Customer request",
      },
      idempotencyKey: "550e8400-e29b-41d4-a716-446655440050",
    });

    expect(
      serviceInstances.utitlitiesService.post,
    ).toHaveBeenCalledWith(
      "/api/payments/payment%2F1/refunds",
      {
        amount: 4,
        reason: "Customer request",
      },
      {
        "Idempotency-Key":
          "550e8400-e29b-41d4-a716-446655440050",
      },
    );
    expect(result).toEqual(response.data);
  });

  it("returns the safe API error message when refunding fails", async () => {
    vi.mocked(
      serviceInstances.utitlitiesService.post,
    ).mockResolvedValue({
      success: false,
      data: null,
      error: {
        code: "payment_not_captured",
        message: "Capture the payment before requesting this refund.",
      },
      meta: {
        correlationId: "correlation-1",
        timestampUtc: "2026-07-23T11:00:00Z",
      },
    });

    await expect(
      paymentService.createPaymentRefund({
        paymentDetailId: "payment-1",
        request: { amount: 4 },
        idempotencyKey:
          "550e8400-e29b-41d4-a716-446655440050",
      }),
    ).rejects.toThrow(
      "Capture the payment before requesting this refund.",
    );
  });
});

describe("stored payment method service", () => {
  it("loads the authenticated shopper's safe payment methods", async () => {
    const methods = [
      {
        paymentMethodId: "method-1",
        type: "scheme",
        brand: "visa",
        lastFour: "1111",
        expiryMonth: "10",
        expiryYear: "2030",
        fundingSource: "CREDIT",
        issuerCountry: "US",
        status: "ACTIVE",
      },
    ];
    vi.mocked(
      serviceInstances.utitlitiesService.get,
    ).mockResolvedValue({
      success: true,
      data: methods,
      error: null,
      meta: {
        correlationId: "correlation-1",
        timestampUtc: "2026-07-23T11:00:00Z",
      },
    });

    const result = await paymentService.getStoredPaymentMethods();

    expect(
      serviceInstances.utitlitiesService.get,
    ).toHaveBeenCalledWith("/api/payments/payment-methods");
    expect(result).toEqual(methods);
  });

  it("treats an empty 204 response as confirmed removal", async () => {
    vi.mocked(
      serviceInstances.utitlitiesService.delete,
    ).mockResolvedValue(undefined);

    const result =
      await paymentService.removeStoredPaymentMethod("method/1");

    expect(
      serviceInstances.utitlitiesService.delete,
    ).toHaveBeenCalledWith(
      "/api/payments/payment-methods/method%2F1",
    );
    expect(result).toBe("removed");
  });

  it("preserves a provider-pending removal outcome", async () => {
    vi.mocked(
      serviceInstances.utitlitiesService.delete,
    ).mockResolvedValue({
      success: true,
      data: {
        paymentMethodId: "method-1",
        status: "REMOVAL_PENDING",
      },
      error: null,
      meta: {
        correlationId: "correlation-1",
        timestampUtc: "2026-07-23T11:00:00Z",
      },
    });

    const result =
      await paymentService.removeStoredPaymentMethod("method-1");

    expect(result).toBe("pending");
  });
});

describe("payment provider service", () => {
  const provider = {
    paymentProviderId: "provider-1",
    version: 4,
    providerName: "ADYEN-ONLINE",
    merchantId: "merchant-1",
    apiBaseUrl: "https://checkout-test.adyen.com/v72",
    returnUrl: "https://payments.example/payments/validate",
    frontendResultUrl: "https://app.example/payment/result",
    countryCode: "CH",
    manualCapture: false,
    maxRefundDays: 365,
    storeId: null,
    isEnabled: true,
  };

  it("loads the tenant provider catalog from the safe endpoint", async () => {
    vi.mocked(
      serviceInstances.utitlitiesService.get,
    ).mockResolvedValue({
      success: true,
      data: [provider],
      error: null,
      meta: {
        correlationId: "correlation-1",
        timestampUtc: "2026-07-29T10:00:00Z",
      },
    });

    const result = await paymentService.getPaymentProviders();

    expect(
      serviceInstances.utitlitiesService.get,
    ).toHaveBeenCalledWith("/api/payments/providers");
    expect(result).toEqual([provider]);
  });

  it("updates only the selected provider with its current version", async () => {
    vi.mocked(
      serviceInstances.utitlitiesService.put,
    ).mockResolvedValue({
      success: true,
      data: { ...provider, version: 5, isEnabled: false },
      error: null,
      meta: {
        correlationId: "correlation-1",
        timestampUtc: "2026-07-29T10:00:00Z",
      },
    });

    await paymentService.updatePaymentProvider({
      paymentProviderId: "provider/1",
      request: {
        version: 4,
        frontendResultUrl: "https://app.example/payment/result",
        countryCode: "CH",
        manualCapture: false,
        maxRefundDays: 365,
        isEnabled: false,
      },
    });

    expect(
      serviceInstances.utitlitiesService.put,
    ).toHaveBeenCalledWith(
      "/api/payments/providers/provider%2F1",
      expect.objectContaining({
        version: 4,
        isEnabled: false,
      }),
    );
  });

  it("sends credential rotation to the explicit rotation endpoint", async () => {
    vi.mocked(
      serviceInstances.utitlitiesService.post,
    ).mockResolvedValue({
      success: true,
      data: { ...provider, version: 5 },
      error: null,
      meta: {
        correlationId: "correlation-1",
        timestampUtc: "2026-07-29T10:00:00Z",
      },
    });

    await paymentService.rotatePaymentProviderCredentials({
      paymentProviderId: "provider-1",
      request: {
        version: 4,
        webhookHmacKey:
          "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      },
    });

    expect(
      serviceInstances.utitlitiesService.post,
    ).toHaveBeenCalledWith(
      "/api/payments/providers/provider-1/rotate",
      expect.objectContaining({
        version: 4,
        webhookHmacKey:
          "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      }),
    );
  });

  describe("stored payment methods", () => {
    it("returns the saved methods", async () => {
      vi.mocked(serviceInstances.utitlitiesService.get).mockResolvedValue({
        success: true,
        data: [{ paymentMethodId: "pm-1" }],
      });

      await expect(paymentService.getStoredPaymentMethods()).resolves.toEqual([
        { paymentMethodId: "pm-1" },
      ]);
    });

    it("raises the reported reason when the read is refused", async () => {
      vi.mocked(serviceInstances.utitlitiesService.get).mockResolvedValue({
        success: false,
        error: { message: "shopper not resolved" },
      });

      await expect(paymentService.getStoredPaymentMethods()).rejects.toThrow(
        "shopper not resolved",
      );
    });

    it("treats a provider-unavailable response as no saved methods", async () => {
      vi.mocked(serviceInstances.utitlitiesService.get).mockResolvedValue({
        success: false,
        data: null,
        error: {
          code: "payment_provider_unavailable",
          message: "The payment provider is temporarily unavailable.",
        },
      });

      await expect(paymentService.getStoredPaymentMethods()).resolves.toEqual(
        [],
      );
    });

    it("treats a rejected provider-unavailable request as no saved methods", async () => {
      vi.mocked(serviceInstances.utitlitiesService.get).mockRejectedValue(
        new Error(
          '{"success":false,"error":{"code":"payment_provider_unavailable","message":"The payment provider is temporarily unavailable."}}',
        ),
      );

      await expect(paymentService.getStoredPaymentMethods()).resolves.toEqual(
        [],
      );
    });

    it("raises a generic reason when the refusal carries none", async () => {
      vi.mocked(serviceInstances.utitlitiesService.get).mockResolvedValue({
        success: false,
      });

      await expect(paymentService.getStoredPaymentMethods()).rejects.toThrow(
        "Saved payment methods could not be loaded.",
      );
    });

    it("raises when the read succeeds but carries no payload", async () => {
      vi.mocked(serviceInstances.utitlitiesService.get).mockResolvedValue({
        success: true,
      });

      await expect(paymentService.getStoredPaymentMethods()).rejects.toThrow();
    });

    it("reports a provider-side removal as pending", async () => {
      vi.mocked(serviceInstances.utitlitiesService.delete).mockResolvedValue({
        success: true,
        data: { paymentMethodId: "pm-1", status: "REMOVAL_PENDING" },
      });

      await expect(
        paymentService.removeStoredPaymentMethod("pm-1"),
      ).resolves.toBe("pending");
    });

    it("reports anything else as removed", async () => {
      vi.mocked(serviceInstances.utitlitiesService.delete).mockResolvedValue({
        success: true,
        data: { paymentMethodId: "pm-1", status: "REMOVED" },
      });

      await expect(
        paymentService.removeStoredPaymentMethod("pm-1"),
      ).resolves.toBe("removed");
    });

    it("treats an empty response as removed", async () => {
      vi.mocked(serviceInstances.utitlitiesService.delete).mockResolvedValue(
        undefined,
      );

      await expect(
        paymentService.removeStoredPaymentMethod("pm-1"),
      ).resolves.toBe("removed");
    });

    it("escapes the identifier it puts in the path", async () => {
      vi.mocked(serviceInstances.utitlitiesService.delete).mockResolvedValue(
        undefined,
      );

      await paymentService.removeStoredPaymentMethod("pm/1 2");

      expect(serviceInstances.utitlitiesService.delete).toHaveBeenCalledWith(
        expect.stringContaining("pm%2F1%202"),
      );
    });
  });

});
