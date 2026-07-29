import { describe, expect, it } from "vitest";
import { createPaymentSchema } from "./create-payment.schema";

const createRequest = (providerName: string) => ({
  providerName,
  amount: 10,
  currencyCode: "CHF",
  orderId: "TEST-ORDER-001",
  rememberCard: false,
  isRecurring: false,
});

describe("createPaymentSchema", () => {
  it.each(["ADYEN-ONLINE", "STRIPE"])(
    "accepts the %s provider",
    (providerName) => {
      expect(
        createPaymentSchema.safeParse(createRequest(providerName)).success,
      ).toBe(true);
    },
  );

  it("rejects an unknown provider", () => {
    expect(
      createPaymentSchema.safeParse(createRequest("UNKNOWN")).success,
    ).toBe(false);
  });
});
