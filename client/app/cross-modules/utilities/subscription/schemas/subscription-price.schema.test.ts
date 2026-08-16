import { describe, expect, it } from "vitest";
import {
  createSubscriptionPriceSchema,
  defaultSubscriptionPriceFormValues,
  FLAT_FEE,
} from "./subscription-price.schema";

describe("createSubscriptionPriceSchema", () => {
  it("accepts the default flat-fee price", () => {
    const result = createSubscriptionPriceSchema.safeParse(
      defaultSubscriptionPriceFormValues,
    );

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.quantityItemKey).toBe(FLAT_FEE);
    }
  });

  it("rejects a negative amount", () => {
    const result = createSubscriptionPriceSchema.safeParse({
      ...defaultSubscriptionPriceFormValues,
      amount: -1,
    });

    expect(result.success).toBe(false);
  });

  it("rejects an interval outside the server's known values", () => {
    const result = createSubscriptionPriceSchema.safeParse({
      ...defaultSubscriptionPriceFormValues,
      interval: 4,
    });

    expect(result.success).toBe(false);
  });

  it("rejects an interval count outside 1..36", () => {
    const result = createSubscriptionPriceSchema.safeParse({
      ...defaultSubscriptionPriceFormValues,
      intervalCount: 37,
    });

    expect(result.success).toBe(false);
  });

  it("coerces a numeric string amount, matching a text input's value type", () => {
    const result = createSubscriptionPriceSchema.safeParse({
      ...defaultSubscriptionPriceFormValues,
      amount: "89.00",
    });

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.amount).toBe(89);
    }
  });
});
