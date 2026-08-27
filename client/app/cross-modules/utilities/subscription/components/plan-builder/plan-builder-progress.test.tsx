import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

const goToStep = vi.fn();

vi.mock("@/components/stepper/stepper-provider", () => ({
  useStepper: () => ({
    completedSteps: [1],
    currentStep: 2,
    totalSteps: 5,
    goToStep: (id: number) => goToStep(id),
    getSteps: () => [
      { id: 1, title: "Identity" },
      { id: 2, title: "Pricing model" },
      { id: 3, title: "What the plan grants" },
      { id: 4, title: "Trial" },
      { id: 5, title: "Review" },
    ],
  }),
}));

import { PlanBuilderProgress } from "./plan-builder-progress";

const progress = () => screen.getByRole("region", { name: "Plan creation progress" });

describe("PlanBuilderProgress — stuck treatment (H3, H5)", () => {
  it("H3: shows the raised treatment only when stuck", () => {
    const { rerender } = render(<PlanBuilderProgress isStuck={false} />);
    expect(progress()).toHaveAttribute("data-stuck", "false");
    expect(progress().className).toContain("shadow-sm");
    expect(progress().className).not.toContain("shadow-lg");

    rerender(<PlanBuilderProgress isStuck />);
    expect(progress()).toHaveAttribute("data-stuck", "true");
    expect(progress().className).toContain("shadow-lg");
  });

  it("H5: drops the treatment again when it returns to normal flow", () => {
    const { rerender } = render(<PlanBuilderProgress isStuck />);
    expect(progress().className).toContain("shadow-lg");

    rerender(<PlanBuilderProgress isStuck={false} />);
    expect(progress().className).not.toContain("shadow-lg");
    expect(progress().className).toContain("shadow-sm");
  });

  it("defaults to unstuck, so the existing single-argument usage is unchanged", () => {
    render(<PlanBuilderProgress />);
    expect(progress()).toHaveAttribute("data-stuck", "false");
  });

  /**
   * This is the constraint that is easy to lose in a later restyle: the measured height of this
   * element positions the preview panel, so the stuck treatment must not change the box. If a
   * future change adds border-width or padding to the stuck variant, the preview will jump every
   * time the user crosses the sticky threshold.
   */
  it("keeps the stuck treatment dimension-neutral (no border-width or padding change)", () => {
    const { rerender } = render(<PlanBuilderProgress isStuck={false} />);
    const unstuck = progress().className;
    rerender(<PlanBuilderProgress isStuck />);
    const stuck = progress().className;

    const boxClasses = (cls: string) =>
      cls
        .split(/\s+/)
        .filter((c) => /^(p[xyltrb]?-|border-\d|sm:p[xy]?-)/.test(c))
        .sort();

    expect(boxClasses(stuck)).toEqual(boxClasses(unstuck));
  });
});

describe("PlanBuilderProgress — behaviour that must not change (C2)", () => {
  it("still reports the step count and renders all five nodes", () => {
    render(<PlanBuilderProgress isStuck />);
    expect(screen.getByText("Step 2 of 5")).toBeInTheDocument();
    expect(screen.getAllByRole("button")).toHaveLength(5);
  });

  it("still navigates via goToStep for reachable steps", async () => {
    render(<PlanBuilderProgress isStuck />);
    await userEvent.click(screen.getByRole("button", { name: /Identity/ }));
    expect(goToStep).toHaveBeenCalledWith(1);
  });

  it("still disables steps that are not yet reachable", () => {
    render(<PlanBuilderProgress />);
    // completedSteps [1], current 2 -> step 4 is not reachable (step 3 incomplete).
    expect(screen.getByRole("button", { name: /Trial/ })).toBeDisabled();
  });

  it("still renders the mobile current-step title line", () => {
    render(<PlanBuilderProgress />);
    const mobileTitle = screen
      .getAllByText("Pricing model")
      .find((el) => el.className.includes("sm:hidden"));
    expect(mobileTitle).toBeDefined();
  });
});
