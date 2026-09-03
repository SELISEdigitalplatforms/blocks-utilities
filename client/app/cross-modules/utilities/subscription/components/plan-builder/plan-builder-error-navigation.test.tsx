import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/**
 * A failed save takes the author to the step that is wrong.
 *
 * Saving happens from the review step, so whatever failed validation is on a step that is not
 * mounted. That has two consequences a test has to hold down: react-hook-form's own
 * focus-first-error does nothing, because the control does not exist; and a message that merely
 * says "some steps have something to fix" leaves the author opening each step in turn.
 */

beforeAll(() => {
  Element.prototype.hasPointerCapture ??= vi.fn(() => false) as never;
  Element.prototype.setPointerCapture ??= vi.fn() as never;
  Element.prototype.releasePointerCapture ??= vi.fn() as never;
  Element.prototype.scrollIntoView ??= vi.fn() as never;
  globalThis.ResizeObserver ??= class {
    observe() {}
    disconnect() {}
    unobserve() {}
  } as unknown as typeof ResizeObserver;
  globalThis.IntersectionObserver ??= class {
    observe() {}
    disconnect() {}
    unobserve() {}
    takeRecords() {
      return [];
    }
  } as unknown as typeof IntersectionObserver;
});

const toast = vi.fn();

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));
vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => ({ data: { data: [], totalCount: 0 } }),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: (...args: unknown[]) => toast(...args) }));

// The step bodies are stubbed so this test is about where the builder sends the author, not about
// any one control. The stepper shell, the form, and the submit path are all real.
vi.mock("./step-identity", () => ({ StepIdentity: () => <div data-testid="step-body" /> }));
vi.mock("./step-pricing-model", () => ({
  StepPricingModel: () => <div data-testid="step-body" />,
}));
vi.mock("./step-usage-limits", () => ({
  StepUsageLimits: () => <div data-testid="step-body" />,
}));
vi.mock("./step-trial", () => ({ StepTrial: () => <div data-testid="step-body" /> }));
vi.mock("./step-review", () => ({ StepReview: () => <div data-testid="step-body" /> }));
vi.mock("../plan-summary-card", () => ({
  PlanSummaryCard: () => <div data-testid="plan-summary-card" />,
}));

import { MemoryRouter } from "react-router";
import { PlanBuilder } from "./plan-builder";
import { defaultSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";
import type { CreateSubscriptionPlanFormValues } from "../../schemas/subscription-plan.schema";

const onSubmit = vi.fn(async () => {});

const renderBuilder = (defaultValues: CreateSubscriptionPlanFormValues) =>
  render(
    <MemoryRouter>
      <PlanBuilder
        mode="create"
        defaultValues={defaultValues}
        title="Create subscription plan"
        description="desc"
        backTo="/plans"
        submitLabel="Create"
        submittingLabel="Creating"
        isSubmitting={false}
        onSubmit={onSubmit}
      />
    </MemoryRouter>,
  );

/** Walks to the review step, where the only way on is Save. */
const goToReview = async (user: ReturnType<typeof userEvent.setup>) => {
  for (let step = 1; step < 5; step++) {
    await user.click(screen.getByRole("button", { name: /next/i }));
  }

  return screen.getByRole("button", { name: /^create$/i });
};

/**
 * Step one is the only step whose Back button is disabled, so this reads the stepper's own state
 * rather than trusting the message beside it.
 */
const isOnFirstStep = () =>
  (screen.getByRole("button", { name: /back/i }) as HTMLButtonElement).disabled;

beforeEach(() => {
  vi.clearAllMocks();
});

describe("a failed save", () => {
  it("goes back to the step holding the problem rather than staying on review", async () => {
    const user = userEvent.setup();
    // The defaults leave code and displayName empty, both of which live on Identity.
    renderBuilder(defaultSubscriptionPlanFormValues);

    const save = await goToReview(user);
    expect(isOnFirstStep()).toBe(false);

    await user.click(save);

    await waitFor(() => expect(isOnFirstStep()).toBe(true));
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("names the step it sent the author to", async () => {
    const user = userEvent.setup();
    renderBuilder(defaultSubscriptionPlanFormValues);

    await user.click(await goToReview(user));

    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.objectContaining({
          variant: "destructive",
          title: expect.stringContaining("Identity"),
        }),
      ),
    );
  });

  /**
   * A problem further in still sends the author forward of step one, so the message and the step
   * agree — and this is the case the old behaviour was worst at, since Identity happened to be
   * where an author would look first anyway.
   */
  it("sends a pricing problem to the pricing step, not to the first step", async () => {
    const user = userEvent.setup();
    renderBuilder({
      ...defaultSubscriptionPlanFormValues,
      code: "pro",
      displayName: "Pro",
      meters: [
        {
          meterKey: "storage-gb",
          displayName: "Storage",
          unitLabel: "GB",
          aggregation: 0,
          resetPolicy: 0,
          quantityScale: 0,
          // Whole units only, so this is refused three steps away from Save.
          includedQuantity: 512.5,
          overageAllowed: true,
          thresholdPercents: [],
          rateTables: [],
        },
      ],
    } as CreateSubscriptionPlanFormValues);

    await user.click(await goToReview(user));

    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.objectContaining({ title: expect.stringContaining("Pricing model") }),
      ),
    );
    expect(isOnFirstStep()).toBe(false);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("still saves when nothing is wrong", async () => {
    const user = userEvent.setup();
    renderBuilder({
      ...defaultSubscriptionPlanFormValues,
      code: "pro",
      displayName: "Pro",
    } as CreateSubscriptionPlanFormValues);

    await user.click(await goToReview(user));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(toast).not.toHaveBeenCalled();
  });
});
