import { render, screen } from "@testing-library/react";
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
