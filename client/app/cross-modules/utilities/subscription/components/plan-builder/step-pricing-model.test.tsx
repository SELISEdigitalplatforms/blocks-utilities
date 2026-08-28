import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { FormProvider, useForm } from "react-hook-form";

import {
  defaultSubscriptionPlanFormValues,
  type CreateSubscriptionPlanFormValues,
} from "../../schemas/subscription-plan.schema";
import { StepPricingModel } from "./step-pricing-model";

const Harness = () => {
  const form = useForm<CreateSubscriptionPlanFormValues>({
    defaultValues: defaultSubscriptionPlanFormValues,
  });

  return (
    <FormProvider {...form}>
      <StepPricingModel />
    </FormProvider>
  );
};

const cardRequirement = () =>
  screen.getByLabelText(
    /Require a payment method before activation, even when nothing is due today/i,
  );

/**
 * These moved here with the control they cover.
 *
 * They were written against `StepTrial`, where the setting used to sit, and stayed there when it
 * was moved to the pricing step — so they failed on every run for a component that was working.
 * They were also invisible: nothing ran the client suite on a pull request, so three permanently
 * red tests cost nobody anything until somebody read the output.
 */
describe("requiring a card before activation", () => {
  /**
   * The setting is not about trials, and a plan with no trial is exactly the case it was added
   * for — a free tier that will one day be billed. So it must be reachable without one.
   */
  it("is offered on a plan with no trial at all", () => {
    render(<Harness />);

    expect(cardRequirement()).toBeInTheDocument();
    expect(cardRequirement()).not.toBeChecked();
  });

  it("explains what changes when it is on", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    expect(screen.getByText(/starts straight away/i)).toBeInTheDocument();

    await user.click(cardRequirement());

    expect(cardRequirement()).toBeChecked();
    expect(screen.getByText(/card form that charges nothing/i)).toBeInTheDocument();
  });

  /**
   * The two card questions are separate settings that read almost identically, and conflating them
   * is the mistake worth guarding: one governs activation of any plan, the other only whether a
   * trial can start. The trial's own question lives in the trial step, so it must not appear here.
   */
  it("is not the trial's own card question", () => {
    render(<Harness />);

    expect(
      screen.queryByLabelText(/Require a card to start the trial/i),
    ).not.toBeInTheDocument();
  });
});
