import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeAll, describe, expect, it, vi } from "vitest";

/**
 * The sticky action bar.
 *
 * The bug this exists to prevent is a plain one: on a long step - the pricing model is by far the
 * worst - Next and Confirm sat at the very bottom of the card, off screen, with nothing to
 * suggest they were there. Anything that returns them to normal flow reintroduces it, and no
 * other test in this module would notice.
 *
 * jsdom has no layout engine and no IntersectionObserver, so what is asserted here is the wiring:
 * the bar carries `sticky bottom-0`, it stays below the Radix portal layer, and it still drives
 * the same four callbacks. Whether it visually pins is confirmed in a real browser.
 */

beforeAll(() => {
  globalThis.IntersectionObserver ??= class {
    observe() {}
    disconnect() {}
    unobserve() {}
    takeRecords() {
      return [];
    }
  } as unknown as typeof IntersectionObserver;
});

import { PlanBuilderActions } from "./plan-builder-actions";

const baseProps = {
  isFirstStep: false,
  isLastStep: false,
  isSubmitting: false,
  submitLabel: "Create plan",
  submittingLabel: "Creating…",
  onBack: vi.fn(),
  onNext: vi.fn(),
  onSubmit: vi.fn(),
};

const bar = () => screen.getByTestId("plan-builder-actions");

describe("PlanBuilderActions — the bar stays reachable", () => {
  it("is sticky to the bottom of the viewport at every breakpoint", () => {
    render(<PlanBuilderActions {...baseProps} />);
    expect(bar().className).toContain("sticky");
    expect(bar().className).toContain("bottom-0");
    // No responsive prefix: a bar that only sticks at some widths is the bug, not the fix.
    expect(bar().className).not.toMatch(/(sm|md|lg|xl):sticky/);
  });

  it("sits below the z-50 Radix Select/Popover portals", () => {
    render(<PlanBuilderActions {...baseProps} />);
    const z = bar().className.match(/z-(\d+)/);
    expect(z).not.toBeNull();
    expect(Number(z![1])).toBeLessThan(50);
  });

  it("spans the card padding so it reads as a footer, not an inset row", () => {
    render(<PlanBuilderActions {...baseProps} />);
    expect(bar().className).toContain("-mx-5");
    expect(bar().className).toContain("sm:-mx-7");
  });

  it("reports its stuck state for the raised treatment", () => {
    render(<PlanBuilderActions {...baseProps} />);
    // False without a real IntersectionObserver, which is the documented degraded path.
    expect(bar()).toHaveAttribute("data-stuck", "false");
  });
});

describe("PlanBuilderActions — behaviour", () => {
  it("advances and retreats through the callbacks it is given", async () => {
    const onNext = vi.fn();
    const onBack = vi.fn();
    render(<PlanBuilderActions {...baseProps} onNext={onNext} onBack={onBack} />);

    await userEvent.click(screen.getByRole("button", { name: /Next/ }));
    expect(onNext).toHaveBeenCalledTimes(1);

    await userEvent.click(screen.getByRole("button", { name: /Back/ }));
    expect(onBack).toHaveBeenCalledTimes(1);
  });

  it("disables Back on the first step", () => {
    render(<PlanBuilderActions {...baseProps} isFirstStep />);
    expect(screen.getByRole("button", { name: /Back/ })).toBeDisabled();
  });

  it("swaps Next for the submit action on the last step", async () => {
    const onSubmit = vi.fn();
    render(<PlanBuilderActions {...baseProps} isLastStep onSubmit={onSubmit} />);

    expect(screen.queryByRole("button", { name: /Next/ })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /Create plan/ }));
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it("locks both controls and shows the progress label while submitting", () => {
    render(<PlanBuilderActions {...baseProps} isLastStep isSubmitting />);
    expect(screen.getByRole("button", { name: /Creating/ })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Back/ })).toBeDisabled();
  });
});
