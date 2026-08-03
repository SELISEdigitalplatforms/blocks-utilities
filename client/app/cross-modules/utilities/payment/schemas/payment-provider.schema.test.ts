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

  const adyenBase = {
    providerName: "ADYEN-ONLINE" as const,
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
  };

  const stripeBase = {
    ...adyenBase,
    providerName: "STRIPE" as const,
    apiBaseUrl: "",
    apiKey: "sk_test_123",
    webhookHmacKey: "whsec_123",
    tokenHmacKey: "",
  };

  const messageFor = (
    result: ReturnType<typeof registerPaymentProviderSchema.safeParse>,
    field: string,
  ) =>
    result.success
      ? undefined
      : result.error.issues.find((issue) => issue.path[0] === field)?.message;

  it("requires Adyen to name its Checkout API base URL", () => {
    const result = registerPaymentProviderSchema.safeParse({
      ...adyenBase,
      apiBaseUrl: "",
    });

    expect(messageFor(result, "apiBaseUrl")).toBe(
      "Adyen requires its Checkout API base URL.",
    );
  });

  it("requires the Adyen webhook HMAC to be 64 hex characters", () => {
    const result = registerPaymentProviderSchema.safeParse({
      ...adyenBase,
      webhookHmacKey: "too-short",
    });

    expect(messageFor(result, "webhookHmacKey")).toBe(
      "Use the 64-character hexadecimal Adyen HMAC key.",
    );
  });

  it("requires Adyen to supply a token webhook HMAC", () => {
    const result = registerPaymentProviderSchema.safeParse({
      ...adyenBase,
      tokenHmacKey: "",
    });

    expect(messageFor(result, "tokenHmacKey")).toBe(
      "Use the 64-character hexadecimal token-webhook HMAC key.",
    );
  });

  it("accepts a Stripe registration with correctly prefixed secrets", () => {
    expect(registerPaymentProviderSchema.safeParse(stripeBase).success).toBe(
      true,
    );
  });

  it.each([["rk_live_1"], ["sk_live_1"]])(
    "accepts the Stripe key prefix %s",
    (apiKey) => {
      expect(
        registerPaymentProviderSchema.safeParse({ ...stripeBase, apiKey })
          .success,
      ).toBe(true);
    },
  );

  it("rejects a Stripe API key without a recognised prefix", () => {
    const result = registerPaymentProviderSchema.safeParse({
      ...stripeBase,
      apiKey: "pk_test_123",
    });

    expect(messageFor(result, "apiKey")).toBe(
      "Stripe API keys start with sk_ or rk_.",
    );
  });

  it("rejects a Stripe endpoint secret without the whsec prefix", () => {
    const result = registerPaymentProviderSchema.safeParse({
      ...stripeBase,
      webhookHmacKey: "secret",
    });

    expect(messageFor(result, "webhookHmacKey")).toBe(
      "Stripe endpoint secrets start with whsec_.",
    );
  });

  it("does not hold Stripe to the Adyen HMAC shape", () => {
    // The Adyen-only rules return early, so they must not leak onto Stripe.
    const result = registerPaymentProviderSchema.safeParse({
      ...stripeBase,
      tokenHmacKey: "not-hex",
    });

    expect(result.success).toBe(true);
  });

  it("requires at least one credential to rotate", () => {
    const result = createRotatePaymentProviderSchema(
      "ADYEN-ONLINE",
    ).safeParse({ apiKey: "", webhookHmacKey: "", tokenHmacKey: "" });

    expect(result.success).toBe(false);
  });

  it("checks the shape of an Adyen secret that is being rotated", () => {
    const result = createRotatePaymentProviderSchema(
      "ADYEN-ONLINE",
    ).safeParse({ apiKey: "", webhookHmacKey: "not-hex", tokenHmacKey: "" });

    expect(result.success).toBe(false);
  });

  it("leaves an omitted Adyen secret alone when rotating another", () => {
    const result = createRotatePaymentProviderSchema(
      "ADYEN-ONLINE",
    ).safeParse({ apiKey: "new-key", webhookHmacKey: "", tokenHmacKey: "" });

    expect(result.success).toBe(true);
  });

  it("checks a rotated Stripe API key prefix", () => {
    const result = createRotatePaymentProviderSchema("STRIPE").safeParse({
      apiKey: "pk_test_1",
      webhookHmacKey: "",
      tokenHmacKey: "",
    });

    expect(result.success).toBe(false);
  });

  it("accepts a correctly prefixed rotated Stripe key", () => {
    const result = createRotatePaymentProviderSchema("STRIPE").safeParse({
      apiKey: "sk_test_1",
      webhookHmacKey: "",
      tokenHmacKey: "",
    });

    expect(result.success).toBe(true);
  });

});
