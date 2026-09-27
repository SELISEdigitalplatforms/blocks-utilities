import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { FormProvider, useForm } from "react-hook-form";

import { defaultSubscriptionPriceFormValues } from "../../schemas/subscription-price.schema";
import {
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
 * The pace fields on a meter.
 *
 * Guards a ceiling nobody can read. The quantity's own label is the only thing naming the window
 * it applies to, and "Most token per window" both reads as a typo and leaves the author to
 * remember which window they picked in the control beside it.
 */
describe("capping how fast a meter is spent", () => {
  const metered = (
    subLimitWindow?: number,
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
        subLimitWindow,
        subLimitQuantity: subLimitWindow === undefined ? undefined : 10,
        subLimitBehaviour: 0,
      },
    ],
  });

  it("names the window the ceiling applies to, not just 'window'", async () => {
    render(<Harness values={metered(0)} />);

    expect(await screen.findByLabelText(/Most tokens per hour/i)).toBeInTheDocument();
  });

  it("says where the window begins, so it is not read as starting at signup", async () => {
    render(<Harness values={metered(2)} />);

    expect(await screen.findByText(/Each week runs Monday to Monday/i)).toBeInTheDocument();
  });

  /**
   * A unit label is authored in the singular — "token", "seat" — and a ceiling is always of more
   * than one.
   */
  it("pluralises the unit the plan was authored with", async () => {
    render(<Harness values={metered(1)} />);

    expect(screen.queryByLabelText(/Most token per/i)).not.toBeInTheDocument();
    expect(await screen.findByLabelText(/Most tokens per day/i)).toBeInTheDocument();
  });
});
