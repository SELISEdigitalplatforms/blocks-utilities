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

  it("defaults the discount combination so an unstated one still means something", () => {
    // A caller that has never heard of the field — an older client, a script, a fixture — has to keep
    // describing the price it meant. BestDiscount is the reading that gives away less.
    const result = createSubscriptionPriceSchema.safeParse({
      ...defaultSubscriptionPriceFormValues,
      quantityDiscountCombination: undefined,
    });

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.quantityDiscountCombination).toBe("BestDiscount");
      expect(result.data.automaticDiscountPercent).toBeUndefined();
    }
  });

  it("reads a cleared discount input as no discount rather than as zero percent", () => {
    // A number input that has been emptied holds "", and coercing that gives 0 — which would author
    // a discount of nothing instead of no discount at all.
    const result = createSubscriptionPriceSchema.safeParse({
      ...defaultSubscriptionPriceFormValues,
      automaticDiscountPercent: "",
    });

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.automaticDiscountPercent).toBeUndefined();
    }
  });

  it("rejects a discount above a hundred percent", () => {
    const result = createSubscriptionPriceSchema.safeParse({
      ...defaultSubscriptionPriceFormValues,
      automaticDiscountPercent: 101,
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
