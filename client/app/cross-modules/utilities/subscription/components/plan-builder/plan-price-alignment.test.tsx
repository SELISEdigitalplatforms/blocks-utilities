import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { FormProvider, useForm } from "react-hook-form";
import type { ReactNode } from "react";

import type { PlanPrice } from "../../models/subscription-plan.model";
import {
  defaultSubscriptionPlanFormValues,
  type CreateSubscriptionPlanFormValues,
} from "../../schemas/subscription-plan.schema";
import { PlanPriceFields } from "./plan-price-fields";

const Harness = ({ children }: { children: ReactNode }) => {
  const form = useForm<CreateSubscriptionPlanFormValues>({
    defaultValues: defaultSubscriptionPlanFormValues,
  });

  return <FormProvider {...form}>{children}</FormProvider>;
};

const renderFields = () =>
  render(
    <Harness>
      <PlanPriceFields />
    </Harness>,
  );

describe("the billing cycle field", () => {
  it("is offered for a monthly price, which is the default cadence", () => {
    renderFields();

    expect(screen.getByLabelText("Billing cycle for price 1")).toBeInTheDocument();
  });

  it("starts on the anniversary, so an author who ignores it changes nothing", () => {
    renderFields();

    expect(screen.getByLabelText("Billing cycle for price 1")).toHaveTextContent(
      "Subscription anniversary",
    );
  });

  /**
   * Hidden rather than disabled. A greyed-out "renew on the 1st" invites the question of how to
   * enable it, and the answer is to sell a different cadence.
   */
  it("disappears once the cadence can no longer carry it", async () => {
    const user = userEvent.setup();
    renderFields();

    const howMany = screen.getByRole("spinbutton", { name: /how many/i });
    await user.clear(howMany);
    await user.type(howMany, "3");

    expect(screen.queryByLabelText("Billing cycle for price 1")).not.toBeInTheDocument();
  });

  it("shows the worked example only once the calendar is actually chosen", async () => {
    const user = userEvent.setup();
    renderFields();

    expect(screen.queryByTestId("billing-alignment-example-0")).not.toBeInTheDocument();

    await user.click(screen.getByLabelText("Billing cycle for price 1"));
    await user.click(screen.getByRole("option", { name: /renew on the 1st/i }));

    expect(screen.getByTestId("billing-alignment-example-0")).toHaveTextContent(
      "A signup on August 25 pays 7/31 of this monthly price, then renews on September 1.",
    );
  });
});

describe("an existing price on the plan", () => {
  const price = (overrides: Partial<PlanPrice> = {}): PlanPrice => ({
    priceId: "price-1",
    currencyCode: "EUR",
    unitAmountMinor: 1_200,
    interval: "Month",
    intervalCount: 1,
    quantityItemKey: null,
    ...overrides,
  });

  it("says when a calendar-aligned one renews", () => {
    render(
      <Harness>
        <PlanPriceFields
          isEditing
          existingPrices={[price({ billingAlignment: "CalendarMonth" })]}
        />
      </Harness>,
    );

    expect(screen.getByText(/every month, on the 1st/)).toBeInTheDocument();
  });

  it("says nothing extra about an anniversary one", () => {
    render(
      <Harness>
        <PlanPriceFields isEditing existingPrices={[price()]} />
      </Harness>,
    );

    expect(screen.getByText(/€12\.00 every month/)).toBeInTheDocument();
    expect(screen.queryByText(/on the 1st/)).not.toBeInTheDocument();
  });
});
