import { zodResolver } from "@hookform/resolvers/zod";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { FormProvider, useForm } from "react-hook-form";

import { defaultSubscriptionPriceFormValues } from "../../schemas/subscription-price.schema";
import {
  createSubscriptionPlanSchema,
  defaultSubscriptionPlanFormValues,
  type CreateSubscriptionPlanFormValues,
} from "../../schemas/subscription-plan.schema";
import { StepPricingModel } from "./step-pricing-model";

const Harness = ({ values }: { values?: Partial<CreateSubscriptionPlanFormValues> }) => {
  const form = useForm<CreateSubscriptionPlanFormValues>({
    defaultValues: { ...defaultSubscriptionPlanFormValues, ...values },
  });

  return (
    <FormProvider {...form}>
      <StepPricingModel />
    </FormProvider>
  );
};

/**
 * The plain {@link Harness} has no resolver, so nothing the schema refuses ever reaches
 * `formState.errors` — every test above renders a field's own live-conditional text, never React
 * Hook Form's own error state. This one wires the same resolver and mode the real plan builder
 * does, because the bug it exists to guard is specific to that machinery: a cross-field error
 * outliving the field that stopped causing it.
 */
const ValidatedHarness = ({ values }: { values?: Partial<CreateSubscriptionPlanFormValues> }) => {
  const form = useForm<CreateSubscriptionPlanFormValues>({
    resolver: zodResolver(createSubscriptionPlanSchema),
    mode: "onBlur",
    defaultValues: { ...defaultSubscriptionPlanFormValues, ...values },
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

/**
 * Which field a place count is read from.
 *
 * Guards telling an author both rules when the price they have already chosen decides it. Both at
 * once reads as a choice they have to make, when it is not one — and the half that does not apply
 * is something they have to recognise as irrelevant before they can act on the half that does.
 * On a flat price the consequence of getting it wrong is silent: the plan saves, and nobody can be
 * given a place on it weeks later.
 */
describe("what decides how many places a plan has", () => {
  const perUnitRule = () => screen.queryByText(/the quantity bought is the answer/i);
  const flatRule = () => screen.queryByText(/is the only number that can/i);

  const userWise = (
    prices: CreateSubscriptionPlanFormValues["prices"],
  ): Partial<CreateSubscriptionPlanFormValues> => ({
    subscriberScope: "User",
    quantityItems: [
      {
        itemKey: "seat",
        unitLabel: "seat",
        minQuantity: 1,
        defaultQuantity: 1,
        quantityDiscountTiers: [],
        countsMembers: true,
      },
    ],
    prices,
  });

  it("says nothing at all on a plan sold to the organization", () => {
    render(<Harness />);

    expect(perUnitRule()).not.toBeInTheDocument();
    expect(flatRule()).not.toBeInTheDocument();
  });

  it("names only the maximum when the plan is priced flat", () => {
    render(<Harness values={userWise([{ ...defaultSubscriptionPriceFormValues }])} />);

    expect(flatRule()).toBeInTheDocument();
    expect(perUnitRule()).not.toBeInTheDocument();
  });

  it("names only the quantity bought when the plan is priced on it", () => {
    render(
      <Harness
        values={userWise([{ ...defaultSubscriptionPriceFormValues, quantityItemKey: "seat" }])}
      />,
    );

    expect(perUnitRule()).toBeInTheDocument();
    expect(flatRule()).not.toBeInTheDocument();
  });

  /**
   * A plan sold two ways, one of them flat.
   */
  /**
   * The maximum is needed if <em>any</em> price could be the one a buyer picks, because whoever
   * picks the flat one pays the same however many people they have — so their quantity cannot be
   * the place count. Reading only the per-seat price here would tell the author a maximum was
   * optional, and the plan would save with nobody able to be given a place on the flat price.
   */
  it("still names the maximum when only one of two prices is flat", () => {
    render(
      <Harness
        values={userWise([
          { ...defaultSubscriptionPriceFormValues, quantityItemKey: "seat" },
          { ...defaultSubscriptionPriceFormValues, interval: 3 },
        ])}
      />,
    );

    expect(flatRule()).toBeInTheDocument();
    expect(perUnitRule()).not.toBeInTheDocument();
  });

  /**
   * Before a price exists there is nothing to go on, and a guess that turned out wrong would have
   * taught the author the opposite of the rule.
   */
  it("gives both rules while no price has been authored", () => {
    render(<Harness values={userWise([])} />);

    expect(perUnitRule()).toBeInTheDocument();
    expect(flatRule()).toBeInTheDocument();
  });
});


/**
 * The pace fields: window, count, rolling, and how they talk to each other.
 */
describe("capping how fast a meter is spent", () => {
  const metered = (
    overrides: Record<string, unknown> = {},
  ): Partial<CreateSubscriptionPlanFormValues> => ({
    meters: [
      {
        meterKey: "token",
        unitLabel: "token",
        includedQuantity: 1000,
        overageAllowed: false,
        quantityScale: 0,
        resetPolicy: 0,
        rates: [],
        thresholdPercents: [],
        subLimitBehaviour: 0,
        subLimitWindowCount: 1,
        subLimitRolling: false,
        ...overrides,
      },
    ],
  });

  it("shows no count or rolling control until a window is chosen", async () => {
    const user = userEvent.setup();

    render(<Harness values={metered()} />);
    await user.click(screen.getByText("Pace (optional)"));

    expect(screen.getByLabelText(/How many windows/i)).toBeDisabled();
    expect(screen.queryByText(/Rolling —/i)).not.toBeInTheDocument();
  });

  it("names the unit in the plural against the window that was chosen", async () => {
    render(<Harness values={metered({ subLimitWindow: 0, subLimitQuantity: 10 })} />);

    expect(await screen.findByLabelText(/Most tokens per hour/i)).toBeInTheDocument();
  });

  it("names the count in the ceiling once more than one window is set", async () => {
    render(
      <Harness
        values={metered({ subLimitWindow: 0, subLimitQuantity: 10, subLimitWindowCount: 5 })}
      />,
    );

    expect(await screen.findByLabelText(/Most tokens per 5 hours/i)).toBeInTheDocument();
  });

  it("offers the rolling checkbox once a window is set, and names the span in it", async () => {
    render(
      <Harness
        values={metered({ subLimitWindow: 0, subLimitQuantity: 10, subLimitWindowCount: 5 })}
      />,
    );

    expect(await screen.findByText(/measure the last 5 hours from right now/i)).toBeInTheDocument();
  });

  it("warns on a fixed hourly count that does not divide a day", async () => {
    render(
      <Harness
        values={metered({ subLimitWindow: 0, subLimitQuantity: 10, subLimitWindowCount: 5 })}
      />,
    );

    expect(await screen.findByText(/has to divide a day evenly/i)).toBeInTheDocument();
  });

  it("says nothing when the fixed hourly count divides a day evenly", () => {
    render(
      <Harness
        values={metered({ subLimitWindow: 0, subLimitQuantity: 10, subLimitWindowCount: 6 })}
      />,
    );

    expect(screen.queryByText(/has to divide a day evenly/i)).not.toBeInTheDocument();
  });

  it("does not warn about dividing a day once the window is marked rolling", () => {
    render(
      <Harness
        values={metered({
          subLimitWindow: 0,
          subLimitQuantity: 10,
          subLimitWindowCount: 5,
          subLimitRolling: true,
        })}
      />,
    );

    expect(screen.queryByText(/has to divide a day evenly/i)).not.toBeInTheDocument();
  });

  /**
   * The stale error this guards: the live-conditional paragraph above hides itself correctly the
   * moment Rolling is checked — but React Hook Form's own error state does not revalidate a field
   * nobody touched, so the field's own {@link FormMessage} and its label, coloured from that same
   * error state, kept naming a rule that had just stopped applying. An author who checked Rolling
   * specifically to satisfy it was told, in the form's own voice, that they had not.
   */
  it("clears the field's own error once rolling is checked, not only the hint text", async () => {
    const user = userEvent.setup();

    // A meter complete enough to parse. Zod skips a plan's cross-field rules when any field fails
    // on type, so a meter missing its name or rate tables would never raise the error at all —
    // and the test would pass on the hint text alone, with or without the fix.
    render(
      <ValidatedHarness
        values={metered({
          displayName: "Tokens",
          aggregation: 0,
          rateTables: [],
          subLimitWindow: 0,
          subLimitQuantity: 10,
          subLimitWindowCount: 5,
        })}
      />,
    );

    const countField = screen.getByLabelText(/How many hours/i);

    await user.click(countField);
    await user.tab();

    // The field's own error state, which the hint paragraph cannot fake.
    await waitFor(() => expect(countField).toHaveAttribute("aria-invalid", "true"));

    await user.click(screen.getByRole("checkbox", { name: /Rolling/i }));

    await waitFor(() => expect(countField).toHaveAttribute("aria-invalid", "false"));
    expect(screen.queryByText(/has to divide a day evenly/i)).not.toBeInTheDocument();
  });

  it("says where a fixed window begins, and nothing about the other two", async () => {
    render(<Harness values={metered({ subLimitWindow: 2, subLimitQuantity: 10 })} />);

    expect(await screen.findByText(/Each week runs Monday to Monday/i)).toBeInTheDocument();
    expect(screen.queryByText(/Each hour runs on the clock/i)).not.toBeInTheDocument();
  });

  it("explains a rolling window differently from a fixed one", async () => {
    render(
      <Harness
        values={metered({
          subLimitWindow: 0,
          subLimitQuantity: 10,
          subLimitRolling: true,
        })}
      />,
    );

    expect(await screen.findByText(/ends now and looks back/i)).toBeInTheDocument();
    expect(screen.queryByText(/Each hour runs on the clock/i)).not.toBeInTheDocument();
  });

  it("clearing the window also clears the count and rolling, not only the cap", async () => {
    const user = userEvent.setup();

    render(
      <Harness
        values={metered({
          subLimitWindow: 0,
          subLimitQuantity: 10,
          subLimitWindowCount: 5,
          subLimitRolling: true,
        })}
      />,
    );

    await user.click(screen.getByLabelText(/^Window$/i));
    await user.click(await screen.findByText(/No pace limit/i));

    expect(screen.queryByLabelText(/How many windows/i)).toBeDisabled();
    expect(screen.queryByText(/Rolling —/i)).not.toBeInTheDocument();
  });
});
