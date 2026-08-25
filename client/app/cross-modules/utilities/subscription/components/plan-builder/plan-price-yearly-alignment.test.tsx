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

const monthly = (overrides: Partial<PlanPrice> = {}): PlanPrice => ({
  priceId: "price-monthly",
  currencyCode: "CHF",
  unitAmountMinor: 95_000,
  interval: "Month",
  intervalCount: 1,
  quantityItemKey: null,
  ...overrides,
});

const Harness = ({
  children,
  existingPrices = [],
}: {
  children?: ReactNode;
  existingPrices?: PlanPrice[];
}) => {
  const form = useForm<CreateSubscriptionPlanFormValues>({
    defaultValues: {
      ...defaultSubscriptionPlanFormValues,
      prices: [
        {
          ...defaultSubscriptionPlanFormValues.prices[0],
          currencyCode: "CHF",
          // Yearly, once a year.
          interval: 3,
          intervalCount: 1,
        },
      ],
    },
  });

  return (
    <FormProvider {...form}>
      {children ?? <PlanPriceFields isEditing existingPrices={existingPrices} />}
    </FormProvider>
  );
};

const chooseCalendar = async () => {
  const user = userEvent.setup();
  await user.click(screen.getByLabelText("Billing cycle for price 1"));
  await user.click(screen.getByRole("option", { name: /start on the 1st/i }));

  return user;
};

describe("a yearly price in the plan builder", () => {
  it("is offered a billing cycle, worded for a year", () => {
    render(<Harness existingPrices={[monthly()]} />);

    expect(screen.getByLabelText("Billing cycle for price 1")).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Billing cycle for price 1" }))
      .toHaveTextContent("Subscription anniversary");
  });

  it("explains that the stub comes first and the year follows", async () => {
    render(<Harness existingPrices={[monthly()]} />);
    await chooseCalendar();

    expect(screen.getByTestId("billing-alignment-example-0")).toHaveTextContent(
      "A signup on August 25 pays 7/31 of the monthly price above, then a full year from September 1.",
    );
  });

  it("asks which monthly price the opening period is charged from", async () => {
    render(<Harness existingPrices={[monthly()]} />);
    await chooseCalendar();

    expect(screen.getByLabelText("Monthly price for price 1")).toBeInTheDocument();
  });

  /**
   * The annual figure is the server's to derive. Offering an editable field would promise an edit
   * that gets overwritten, which is worse than never inviting it.
   */
  it("shows the annual amount as derived rather than entered", async () => {
    render(<Harness existingPrices={[monthly()]} />);
    const user = await chooseCalendar();

    expect(screen.getByLabelText("Amount for price 1")).toHaveAttribute("readonly");

    await user.click(screen.getByLabelText("Monthly price for price 1"));
    await user.click(screen.getByRole("option", { name: /950\.00/ }));

    expect(screen.getByTestId("stub-basis-preview-0")).toHaveTextContent(
      /CHF\s*11.400\.00 a year/,
    );
  });

  it("says so when the plan has no monthly price to charge from", async () => {
    render(<Harness existingPrices={[]} />);
    await chooseCalendar();

    expect(screen.getByTestId("stub-basis-empty-0")).toHaveTextContent(
      /no monthly CHF price yet/i,
    );
  });

  it("offers only monthly prices in the same currency", async () => {
    render(
      <Harness
        existingPrices={[
          monthly(),
          monthly({ priceId: "price-eur", currencyCode: "EUR" }),
          monthly({ priceId: "price-yearly-other", interval: "Year" }),
        ]}
      />,
    );
    const user = await chooseCalendar();
    await user.click(screen.getByLabelText("Monthly price for price 1"));

    expect(screen.getAllByRole("option")).toHaveLength(1);
  });

  it("asks nothing about a monthly counterpart while the price is on its anniversary", () => {
    render(<Harness existingPrices={[monthly()]} />);

    expect(screen.queryByLabelText("Monthly price for price 1")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Amount for price 1")).not.toHaveAttribute("readonly");
  });
});
