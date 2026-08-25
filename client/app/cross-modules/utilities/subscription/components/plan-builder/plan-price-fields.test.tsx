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

const Harness = ({
  children,
  onValues,
}: {
  children: ReactNode;
  /** Called on every render with what the form holds, so a test can assert the submitted shape. */
  onValues?: (values: CreateSubscriptionPlanFormValues) => void;
}) => {
  const form = useForm<CreateSubscriptionPlanFormValues>({
    defaultValues: defaultSubscriptionPlanFormValues,
  });

  onValues?.(form.watch());

  return <FormProvider {...form}>{children}</FormProvider>;
};

const price = (overrides: Partial<PlanPrice> = {}): PlanPrice => ({
  priceId: "price-1",
  currencyCode: "EUR",
  unitAmountMinor: 1_200,
  interval: "Month",
  intervalCount: 1,
  quantityItemKey: "team-members",
  ...overrides,
});

describe("PlanPriceFields", () => {
  it("shows what the plan is already sold on while editing", () => {
    // An author who cannot see the monthly price already there is the one who adds a second.
    render(
      <Harness>
        <PlanPriceFields isEditing existingPrices={[price()]} />
      </Harness>,
    );

    expect(screen.getByText(/Already on this plan/)).toBeInTheDocument();
    expect(screen.getByText(/€12\.00 per team-members/)).toBeInTheDocument();
  });

  it("lists every existing price, not just the first", () => {
    render(
      <Harness>
        <PlanPriceFields
          isEditing
          existingPrices={[
            price(),
            price({
              priceId: "price-2",
              unitAmountMinor: 12_000,
              interval: "Year",
            }),
          ]}
        />
      </Harness>,
    );

    expect(screen.getByText(/€12\.00 per team-members/)).toBeInTheDocument();
    expect(screen.getByText(/€120\.00 per team-members/)).toBeInTheDocument();
  });

  it("says nothing about existing prices when a plan has none", () => {
    render(
      <Harness>
        <PlanPriceFields isEditing existingPrices={[]} />
      </Harness>,
    );

    expect(screen.queryByText(/Already on this plan/)).not.toBeInTheDocument();
  });

  it("stays quiet about tax until a rate is entered", () => {
    // Most prices carry no tax. A selector and a preview on every card would make an author answer
    // a VAT question to sell a plan in a country that has none.
    render(
      <Harness>
        <PlanPriceFields />
      </Harness>,
    );

    expect(screen.getByLabelText(/Tax rate for price 1/)).toBeInTheDocument();
    expect(screen.queryByLabelText(/Tax mode for price 1/)).not.toBeInTheDocument();
    expect(screen.queryByTestId("tax-preview-0")).not.toBeInTheDocument();
  });

  it("spells out an exclusive price as a sum once a rate is typed", async () => {
    render(
      <Harness>
        <PlanPriceFields />
      </Harness>,
    );

    // Typed into the real inputs rather than pushed through the form: a number input holds a
    // string, and every rounding bug this preview could have starts with "145" + 7.7.
    await userEvent.clear(screen.getByLabelText(/^Amount$/));
    await userEvent.type(screen.getByLabelText(/^Amount$/), "145");
    await userEvent.type(screen.getByLabelText(/Tax rate for price 1/), "7.7");

    // USD by default in the builder, so the figures are dollars — the arithmetic is the point.
    expect(screen.getByTestId("tax-preview-0")).toHaveTextContent("$145.00");
    expect(screen.getByTestId("tax-preview-0")).toHaveTextContent("$11.17");
    expect(screen.getByTestId("tax-preview-0")).toHaveTextContent("$156.17");
  });

  it("says the customer pays the configured amount when tax is included", async () => {
    render(
      <Harness>
        <PlanPriceFields />
      </Harness>,
    );

    await userEvent.clear(screen.getByLabelText(/^Amount$/));
    await userEvent.type(screen.getByLabelText(/^Amount$/), "145");
    await userEvent.type(screen.getByLabelText(/Tax rate for price 1/), "7.7");
    await userEvent.click(screen.getByLabelText(/Tax mode for price 1/));
    await userEvent.click(screen.getByRole("option", { name: /Already in the price/ }));

    const preview = screen.getByTestId("tax-preview-0");

    // The same 145, read the other way: the total does not move and the tax comes out of it.
    expect(preview).toHaveTextContent("including");
    expect(preview).toHaveTextContent("$145.00");
    expect(preview).toHaveTextContent("$10.37");
  });

  it("holds no rate at all when the field is cleared", async () => {
    // The trap a coerced number field walks into: an emptied input holds "", which becomes 0, which
    // authors a zero-rate tax instead of an untaxed price.
    let latest: CreateSubscriptionPlanFormValues | undefined;

    render(
      <Harness onValues={(values) => { latest = values; }}>
        <PlanPriceFields />
      </Harness>,
    );

    const rate = screen.getByLabelText(/Tax rate for price 1/);

    await userEvent.type(rate, "7.7");
    await userEvent.clear(rate);

    expect(latest?.prices[0]?.taxPercent).toBeFalsy();
    expect(screen.queryByTestId("tax-preview-0")).not.toBeInTheDocument();
  });

  it("names the tax on prices the plan already has", () => {
    // A reader of the read-only list cannot otherwise tell whether €12.00 is what a customer pays.
    render(
      <Harness>
        <PlanPriceFields
          isEditing
          existingPrices={[
            price({ taxRateBasisPoints: 770, taxMode: "Inclusive" }),
            price({ priceId: "price-2", taxRateBasisPoints: 2_000, taxMode: "Exclusive" }),
          ]}
        />
      </Harness>,
    );

    expect(screen.getByText(/incl\. 7\.7% tax/)).toBeInTheDocument();
    expect(screen.getByText(/\+ 20% tax/)).toBeInTheDocument();
  });

  it("shows what an automatic discount does to the charge", async () => {
    // The whole reason the preview exists: 8% off 89.00 is a number an author can check against the
    // pricing page they wrote, and a percentage on its own is not.
    render(
      <Harness>
        <PlanPriceFields />
      </Harness>,
    );

    await userEvent.type(screen.getByLabelText(/^Amount$/), "89");
    await userEvent.type(screen.getByLabelText(/Automatic discount for price 1/), "8");

    const preview = screen.getByTestId("discount-preview-0");

    expect(preview).toHaveTextContent("$7.12");
    expect(preview).toHaveTextContent("$81.88");
  });

  it("offers the combination only once there is a discount to combine", async () => {
    render(
      <Harness>
        <PlanPriceFields />
      </Harness>,
    );

    expect(
      screen.queryByLabelText(/Discount combination for price 1/),
    ).not.toBeInTheDocument();

    await userEvent.type(screen.getByLabelText(/Automatic discount for price 1/), "8");

    expect(screen.getByLabelText(/Discount combination for price 1/)).toBeInTheDocument();
  });

  it("holds no discount at all when the field is cleared", async () => {
    // The same trap the tax rate walks into: an emptied number input holds "", which coerces to 0,
    // which authors a discount of nothing rather than no discount.
    let latest: CreateSubscriptionPlanFormValues | undefined;

    render(
      <Harness onValues={(values) => { latest = values; }}>
        <PlanPriceFields />
      </Harness>,
    );

    const discount = screen.getByLabelText(/Automatic discount for price 1/);

    await userEvent.type(discount, "8");
    await userEvent.clear(discount);

    expect(latest?.prices[0]?.automaticDiscountPercent).toBeFalsy();
    expect(screen.queryByTestId("discount-preview-0")).not.toBeInTheDocument();
  });

  it("offers a discount editor for prices the plan already has", () => {
    // Editable, unlike the amount beside it: the discount reaches future subscriptions only, so
    // changing it cannot contradict anything already sold.
    render(
      <Harness>
        <PlanPriceFields
          isEditing
          existingPrices={[price({ automaticDiscountBasisPoints: 800 })]}
          onUpdatePriceDiscount={async () => {}}
        />
      </Harness>,
    );

    // Named by the price itself, which is also what tells it apart from the draft row's own
    // field further down the form.
    expect(screen.getByLabelText(/Automatic discount for €12\.00/)).toHaveValue(8);
    expect(screen.getByRole("button", { name: /Save discount/ })).toBeInTheDocument();
  });

  it("says nothing about existing prices while creating", () => {
    render(
      <Harness>
        <PlanPriceFields />
      </Harness>,
    );

    expect(screen.queryByText(/Already on this plan/)).not.toBeInTheDocument();
    expect(screen.getByText(/How much does it cost\?/)).toBeInTheDocument();
  });
});
