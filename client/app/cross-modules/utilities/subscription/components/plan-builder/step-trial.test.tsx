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

const trialCardQuestion = () => screen.getByLabelText(/Require a card to start the trial/i);

/**
 * The trial's own card question, which is not the plan's.
 *
 * Two settings here read almost the same and mean different things: this one decides whether a
 * trial can begin without a card, and the one in the pricing step decides whether any plan can be
 * activated without one. This file used to test both and now tests the one that lives here — the
 * other moved with its control.
 */
describe("the trial's card requirement", () => {
  it("is on by default, and says the first period is charged", () => {
    render(<Harness />);

    expect(trialCardQuestion()).toBeChecked();
    // The consequence, not the setting: a card cannot be held without charging it, so requiring
    // one turns the trial into an ordinary paid period with the allowances below.
    expect(screen.getByText(/first period is charged at signup/i)).toBeInTheDocument();
  });

  it("explains what a genuinely free trial leaves the product to do", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(trialCardQuestion());

    expect(trialCardQuestion()).not.toBeChecked();
    expect(screen.getByText(/Genuinely free until the trial ends/i)).toBeInTheDocument();
  });

  /**
   * The plan-wide setting is deliberately not here: a card requirement at signup is a billing
   * decision rather than a trial one. Asserted so the two do not drift back together.
   */
  it("does not carry the plan-wide payment-method setting", () => {
    render(<Harness />);

    expect(
      screen.queryByLabelText(/Require a payment method before activation/i),
    ).not.toBeInTheDocument();
  });
});
