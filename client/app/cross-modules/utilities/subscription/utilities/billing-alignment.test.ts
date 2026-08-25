import { describe, expect, it } from "vitest";

import { isCalendarEligible } from "./billing-alignment";
import { defaultSubscriptionPriceFormValues } from "../schemas/subscription-price.schema";
import { createSubscriptionPriceSchema } from "../schemas/subscription-price.schema";

describe("isCalendarEligible", () => {
  it("accepts a price billed every single month", () => {
    expect(isCalendarEligible({ interval: 2, intervalCount: 1 })).toBe(true);
  });

  it.each([
    ["quarterly", 2, 3],
    ["annually as twelve months", 2, 12],
    ["daily", 0, 1],
    ["fortnightly", 1, 2],
    ["yearly", 3, 1],
  ])("refuses %s, which has no single first to renew on", (_label, interval, intervalCount) => {
    expect(isCalendarEligible({ interval, intervalCount })).toBe(false);
  });
});

describe("the price schema's alignment field", () => {
  it("defaults to the anniversary, so an author who ignores it changes nothing", () => {
    expect(defaultSubscriptionPriceFormValues.billingAlignment).toBe("Anniversary");
  });

  it("reads an omitted alignment as the anniversary", () => {
    const parsed = createSubscriptionPriceSchema.parse({
      currencyCode: "chf",
      amount: 89,
      interval: 2,
      intervalCount: 1,
      quantityItemKey: "seat",
    });

    expect(parsed.billingAlignment).toBe("Anniversary");
  });

  it("keeps a calendar alignment the author chose", () => {
    const parsed = createSubscriptionPriceSchema.parse({
      currencyCode: "CHF",
      amount: 89,
      interval: 2,
      intervalCount: 1,
      quantityItemKey: "seat",
      billingAlignment: "CalendarMonth",
    });

    expect(parsed.billingAlignment).toBe("CalendarMonth");
  });

  it("rejects an alignment it has never heard of", () => {
    const result = createSubscriptionPriceSchema.safeParse({
      currencyCode: "CHF",
      amount: 89,
      interval: 2,
      intervalCount: 1,
      quantityItemKey: "seat",
      billingAlignment: "FinancialQuarter",
    });

    expect(result.success).toBe(false);
  });
});
