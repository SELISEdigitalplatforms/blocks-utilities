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
  const limit = (overrides: Record<string, unknown> = {}) => ({
    window: 0,
    count: 1,
    rolling: false,
    quantity: 10,
    behaviour: 0,
    ...overrides,
  });

  /**
   * A meter complete enough to parse. Zod skips a plan's cross-field rules when any field fails
   * on type, so a meter missing its name or rate tables would never raise those errors at all.
   */
  const metered = (
    subLimits: Record<string, unknown>[] = [],
    overrides: Record<string, unknown> = {},
  ): Partial<CreateSubscriptionPlanFormValues> => ({
    meters: [
      {
        meterKey: "token",
        displayName: "Tokens",
        unitLabel: "token",
        aggregation: 0,
        includedQuantity: 1000,
        overageAllowed: false,
        quantityScale: 0,
        resetPolicy: 0,
        rateTables: [],
        thresholdPercents: [],
        subLimits,
        ...overrides,
      },
    ] as CreateSubscriptionPlanFormValues["meters"],
  });

  const rows = () => screen.queryAllByTestId("pace-limit");

  it("starts with no limit and a way to add one", async () => {
    render(<Harness values={metered()} />);
    await userEvent.setup().click(screen.getByText(/Pace limits \(optional\)/));

    expect(rows()).toHaveLength(0);
    expect(screen.getByRole("button", { name: /Add limit/ })).toBeEnabled();
  });

  it("adds limits up to three, and no more", async () => {
    const user = userEvent.setup();

    render(<Harness values={metered([limit()])} />);

    await user.click(screen.getByRole("button", { name: /Add limit/ }));
    await user.click(screen.getByRole("button", { name: /Add limit/ }));

    expect(rows()).toHaveLength(3);
    expect(screen.getByRole("button", { name: /Add limit/ })).toBeDisabled();
  });

  it("removes a limit", async () => {
    const user = userEvent.setup();

    render(<Harness values={metered([limit(), limit({ window: 2, quantity: 500 })])} />);

    await user.click(screen.getByRole("button", { name: "Remove the week limit" }));

    expect(rows()).toHaveLength(1);
  });

  it("names the unit in the plural against each limit's own span", () => {
    render(<Harness values={metered([limit({ count: 5, rolling: true }), limit({ window: 2, quantity: 500 })])} />);

    expect(screen.getByText("Most tokens per 5 hours")).toBeInTheDocument();
    expect(screen.getByText("Most tokens per week")).toBeInTheDocument();
  });

  it("says where each fixed window begins, and how a rolling one measures", () => {
    render(<Harness values={metered([limit({ count: 5, rolling: true }), limit({ window: 2, quantity: 500 })])} />);

    expect(screen.getByText(/ends now and looks back/i)).toBeInTheDocument();
    expect(screen.getByText(/Each week runs Monday to Monday/i)).toBeInTheDocument();
    expect(screen.queryByText(/Each hour runs on the clock/i)).not.toBeInTheDocument();
  });

  /** The hourly limit caps a day at about 25,000, so a 50,000 daily limit can never apply. */
  it("warns when a longer limit can never be reached", () => {
    render(<Harness values={metered([limit({ quantity: 1000 }), limit({ window: 1, quantity: 50_000 })], { includedQuantity: 10_000_000 })} />);

    expect(screen.getByText(/this one never applies/)).toBeInTheDocument();
  });

  it("warns when the limits leave the included amount out of reach", () => {
    render(<Harness values={metered([limit({ window: 2, quantity: 20_000 })], { includedQuantity: 1_000_000 })} />);

    expect(screen.getByText(/can never all be used/)).toBeInTheDocument();
  });

  it("does not warn about a reporting limit leaving the included amount out of reach", () => {
    render(<Harness values={metered([limit({ window: 2, quantity: 20_000, behaviour: 1 })], { includedQuantity: 1_000_000 })} />);

    expect(screen.queryByText(/can never all be used/)).not.toBeInTheDocument();
  });

  /**
   * The divide-a-day rule is filed on the count but fixed by the rolling box. React Hook Form
   * only revalidates the field that changed, so without the re-check the count stayed red after
   * the click that made it valid.
   */
  it("clears the count's error once rolling makes it valid", async () => {
    const user = userEvent.setup();

    render(<ValidatedHarness values={metered([limit({ count: 5 })])} />);

    const count = screen.getByLabelText(/How many hours/i);

    await user.click(count);
    await user.tab();
    await waitFor(() => expect(count).toHaveAttribute("aria-invalid", "true"));

    await user.click(screen.getByRole("checkbox", { name: /Rolling/i }));

    await waitFor(() => expect(count).toHaveAttribute("aria-invalid", "false"));
  });

  /** The same-length error sits on one row and is fixed on the other. */
  it("clears one row's error when the other row is changed to fix it", async () => {
    const user = userEvent.setup();

    render(<ValidatedHarness values={metered([limit({ count: 24 }), limit({ window: 1, quantity: 500 })])} />);

    const [, secondCount] = screen.getAllByLabelText(/How many/i);

    await user.click(secondCount);
    await user.tab();
    await waitFor(() => expect(secondCount).toHaveAttribute("aria-invalid", "true"));

    const firstCount = screen.getAllByLabelText(/How many/i)[0];
    await user.clear(firstCount);
    await user.type(firstCount, "6");

    await waitFor(() => expect(secondCount).toHaveAttribute("aria-invalid", "false"));
  });
});
