import { describe, expect, it } from "vitest";
import {
  createRotatePaymentProviderSchema,
  registerPaymentProviderSchema,
  updatePaymentProviderSchema,
} from "./payment-provider.schema";

const hmac =
  "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

describe("payment provider schemas", () => {
  it("accepts an Adyen registration with correctly shaped HMAC keys", () => {
    const result = registerPaymentProviderSchema.safeParse({
      providerName: "ADYEN-ONLINE",
      merchantId: "merchant-1",
      frontendResultUrl: "https://app.example/payment/result",
      apiBaseUrl: "https://checkout-test.adyen.com/v72",
      countryCode: "CH",
      manualCapture: false,
      maxRefundDays: 365,
      storeId: "",
      apiKey: "adyen-api-key",
      webhookHmacKey: hmac,
      tokenHmacKey: hmac,
    });

    expect(result.success).toBe(true);
  });

  it("rejects malformed Adyen HMAC material before submission", () => {
    const result = registerPaymentProviderSchema.safeParse({
      providerName: "ADYEN-ONLINE",
      merchantId: "merchant-1",
      frontendResultUrl: "https://app.example/payment/result",
      apiBaseUrl: "https://checkout-test.adyen.com/v72",
      countryCode: "CH",
      manualCapture: false,
      maxRefundDays: 365,
      storeId: "",
      apiKey: "adyen-api-key",
      webhookHmacKey: "not-hex",
      tokenHmacKey: "not-hex",
    });

    expect(result.success).toBe(false);
  });

  it("requires at least one credential during rotation", () => {
    const result = createRotatePaymentProviderSchema(
      "ADYEN-ONLINE",
    ).safeParse({
      apiKey: "",
      webhookHmacKey: "",
      tokenHmacKey: "",
    });

    expect(result.success).toBe(false);
  });

  it("rejects non-HTTPS frontend destinations on update", () => {
    const result = updatePaymentProviderSchema.safeParse({
      frontendResultUrl: "http://app.example/payment/result",
      countryCode: "CH",
      manualCapture: false,
      maxRefundDays: 365,
      storeId: "",
      isEnabled: true,
    });

    expect(result.success).toBe(false);
  });
});
