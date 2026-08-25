import { describe, expect, it, vi } from "vitest";

import type {
  CreateSubscriptionPriceRequest,
  SubscriptionPlan,
} from "../models/subscription-plan.model";
import type { CreateSubscriptionPriceFormValues } from "../schemas/subscription-price.schema";
import { defaultSubscriptionPriceFormValues } from "../schemas/subscription-price.schema";
import { submitPlanWithPrices } from "./submit-plan-with-prices";

const plan = { planId: "plan-1", organizationId: null } as unknown as SubscriptionPlan;

const submit = async (...prices: Partial<CreateSubscriptionPriceFormValues>[]) => {
  const createPrice = vi.fn<(request: CreateSubscriptionPriceRequest) => Promise<SubscriptionPlan>>(
    () => Promise.resolve(plan),
  );

  await submitPlanWithPrices({
    planRequest: {},
    prices: prices.map((price) => ({
      ...defaultSubscriptionPriceFormValues,
      quantityItemKey: "seat",
      amount: 89,
      ...price,
    })),
    createPlan: () => Promise.resolve(plan),
    createPrice,
  });

  return createPrice.mock.calls.map(([request]) => request);
};

describe("submitting a plan's prices", () => {
  it("sends the calendar alignment for a monthly price", async () => {
    const [request] = await submit({
      interval: 2,
      intervalCount: 1,
      billingAlignment: "CalendarMonth",
    });

    expect(request.billingAlignment).toBe("CalendarMonth");
  });

  /**
   * The combination the server refuses outright, and the one a form can drift into: an author
   * picks the calendar on a monthly price and then changes the cadence to quarterly. The field
   * stops being shown, but the value it held is still in the form.
   */
  it("drops a calendar alignment the cadence can no longer carry", async () => {
    const [request] = await submit({
      interval: 2,
      intervalCount: 3,
      billingAlignment: "CalendarMonth",
    });

    expect(request.billingAlignment).toBeUndefined();
    expect(request.intervalCount).toBe(3);
  });

  it("sends the calendar alignment for a yearly price too", async () => {
    const [request] = await submit({
      interval: 3,
      intervalCount: 1,
      billingAlignment: "CalendarMonth",
      calendarStubBasePriceId: "price-monthly",
    });

    expect(request.billingAlignment).toBe("CalendarMonth");
    expect(request.calendarStubBasePriceId).toBe("price-monthly");
    expect(request.unitAmountMinor).toBeUndefined();
  });

  it("sends nothing for a price billed every two years", async () => {
    const [request] = await submit({
      interval: 3,
      intervalCount: 2,
      billingAlignment: "CalendarMonth",
    });

    expect(request.billingAlignment).toBeUndefined();
  });

  /**
   * The monthly link only means something on a calendar-aligned yearly price, and the server
   * refuses it anywhere else. A form can drift into that state by an author choosing yearly,
   * picking a counterpart, then switching the cadence back to monthly.
   */
  it("drops a monthly counterpart the cadence can no longer carry", async () => {
    const [request] = await submit({
      interval: 2,
      intervalCount: 1,
      billingAlignment: "CalendarMonth",
      calendarStubBasePriceId: "price-monthly",
    });

    expect(request.calendarStubBasePriceId).toBeUndefined();
    expect(request.unitAmountMinor).toBeDefined();
  });

  it("sends the anniversary explicitly when that is what was chosen", async () => {
    const [request] = await submit({
      interval: 2,
      intervalCount: 1,
      billingAlignment: "Anniversary",
    });

    expect(request.billingAlignment).toBe("Anniversary");
  });

  it("carries each price's own alignment when a plan sells several", async () => {
    const requests = await submit(
      { interval: 2, intervalCount: 1, billingAlignment: "CalendarMonth" },
      { interval: 3, intervalCount: 1, billingAlignment: "Anniversary" },
    );

    expect(requests.map((request) => request.billingAlignment)).toEqual([
      "CalendarMonth",
      "Anniversary",
    ]);
  });
});
