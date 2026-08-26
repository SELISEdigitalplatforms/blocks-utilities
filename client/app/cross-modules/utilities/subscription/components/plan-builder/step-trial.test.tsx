import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { FormProvider, useForm } from "react-hook-form";

import {
  defaultSubscriptionPlanFormValues,
  type CreateSubscriptionPlanFormValues,
} from "../../schemas/subscription-plan.schema";
import { StepTrial } from "./step-trial";

const Harness = () => {
  const form = useForm<CreateSubscriptionPlanFormValues>({
    defaultValues: defaultSubscriptionPlanFormValues,
  });

  return (
    <FormProvider {...form}>
      <StepTrial />
    </FormProvider>
  );
};

const cardRequirement = () =>
  screen.getByLabelText(
    /Require a payment method before activation, even when nothing is due today/i,
  );

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

  it("leaves the trial's own card question alone", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(cardRequirement());

    expect(screen.getByLabelText(/Require a card to start the trial/i)).toBeChecked();
  });
});
