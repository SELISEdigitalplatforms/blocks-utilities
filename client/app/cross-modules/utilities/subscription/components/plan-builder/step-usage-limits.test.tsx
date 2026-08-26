import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { FormProvider, useForm } from "react-hook-form";
import type { ReactNode } from "react";
import {
  defaultSubscriptionPlanFormValues,
  type CreateSubscriptionPlanFormValues,
} from "../../schemas/subscription-plan.schema";
import { StepUsageLimits } from "./step-usage-limits";

const Harness = ({
  children,
  defaults,
}: {
  children: ReactNode;
  defaults?: Partial<CreateSubscriptionPlanFormValues>;
}) => {
  const form = useForm<CreateSubscriptionPlanFormValues>({
    defaultValues: { ...defaultSubscriptionPlanFormValues, ...defaults },
  });

  return <FormProvider {...form}>{children}</FormProvider>;
};

const meter = {
  meterKey: "ses-signatures",
  displayName: "Simple Signatures (SES)",
  unitLabel: "signature",
  aggregation: 0,
  includedQuantity: 150,
  overageAllowed: true,
  thresholdPercents: [],
  rateTables: [],
};

const renderStep = () =>
  render(
    <Harness defaults={{ meters: [meter] }}>
      <StepUsageLimits />
    </Harness>,
  );

const addLimit = () => fireEvent.click(screen.getByText("Add usage limit"));

const selectKind = (card: HTMLElement, optionLabel: string | RegExp) => {
  // Radix Select renders a button, not a native <select>, so the option list has to be opened
  // before the choice exists in the DOM.
  fireEvent.click(card.querySelector("[role='combobox']")!);
  fireEvent.click(screen.getByText(optionLabel));
};

const cards = () => screen.getAllByLabelText("Remove").map((button) => button.closest("div")!);

describe("StepUsageLimits", () => {
  it("reveals the meter and limit fields when a card's own kind becomes Count", async () => {
    renderStep();
    addLimit();

    expect(screen.queryByText("Limit")).not.toBeInTheDocument();

    selectKind(cards()[0], /^Count/);

    await waitFor(() => {
      expect(screen.getByText("Draws down which meter")).toBeInTheDocument();
      expect(screen.getByText("Limit")).toBeInTheDocument();
    });
  });

  /**
   * The regression this guards: useFieldArray's `fields` is a snapshot refreshed only when the
   * array itself changes, so reading the kind from it left a second card's dependent fields
   * hidden forever — the first card looked fine purely because appending the second one happened
   * to re-snapshot it.
   */
  it("reveals them on a later card too, not just the first", async () => {
    renderStep();
    addLimit();
    addLimit();

    selectKind(cards()[1], /^Count/);

    await waitFor(() => {
      expect(screen.getByText("Draws down which meter")).toBeInTheDocument();
      expect(screen.getByText("Limit")).toBeInTheDocument();
    });
  });

  it("hides them again when the kind moves away from Count", async () => {
    renderStep();
    addLimit();

    selectKind(cards()[0], /^Count/);
    await waitFor(() => expect(screen.getByText("Limit")).toBeInTheDocument());

    selectKind(cards()[0], /^Unlimited/);

    await waitFor(() => {
      expect(screen.queryByText("Draws down which meter")).not.toBeInTheDocument();
      expect(screen.queryByText("Limit")).not.toBeInTheDocument();
    });
  });

  it("offers the plan's own meters as the drawdown source", async () => {
    renderStep();
    addLimit();

    selectKind(cards()[0], /^Count/);
    await waitFor(() => expect(screen.getByText("Draws down which meter")).toBeInTheDocument());

    fireEvent.click(screen.getByText("Choose a meter"));

    expect(await screen.findByText("Simple Signatures (SES)")).toBeInTheDocument();
  });
});
