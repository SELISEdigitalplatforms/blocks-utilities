import { render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/**
 * Layout wiring for the sticky stepper and preview panel.
 *
 * These assertions look shallow but they guard the crux of the fix: the ONLY reason the preview
 * column's pre-existing `sticky` class did nothing was `overflow-hidden` on <main>. If a future
 * change puts it back - or drops the clip layer that replaced it - the feature silently reverts,
 * with no test failing anywhere else and nothing visible in jsdom.
 *
 * jsdom has no layout engine, so what cannot be asserted here is whether the page actually sticks.
 * That is confirmed in a real browser instead, and reported separately.
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

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));
vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => ({ data: { data: [], totalCount: 0 } }),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));

// The step bodies are large and irrelevant to layout; stubbing them keeps this test about the shell.
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

const baseProps = {
  mode: "create" as const,
  defaultValues: undefined as never,
  title: "Create subscription plan",
  description: "desc",
  backTo: "/plans",
  submitLabel: "Create",
  submittingLabel: "Creating",
  isSubmitting: false,
  onSubmit: vi.fn(async () => {}),
};

const renderBuilder = () =>
  render(
    <MemoryRouter>
      <PlanBuilder {...baseProps} />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
});

describe("plan builder sticky layout", () => {
  it("H6/F1: <main> no longer clips, which is what made sticky inert", () => {
    renderBuilder();
    const main = document.querySelector("main");
    expect(main).not.toBeNull();
    expect(main!.className).not.toContain("overflow-hidden");
  });

  it("H6: the decorative blurs are still clipped, by a dedicated layer", () => {
    renderBuilder();
    const decoration = screen.getByTestId("plan-builder-decoration");
    expect(decoration.className).toContain("overflow-hidden");
    expect(decoration.className).toContain("absolute");
    expect(decoration.className).toContain("inset-0");
    // The invariant, not the census: the page's own decorative blurs live inside the clip layer,
    // and none is left loose as a child of <main>, where an unclipped blur would widen the page.
    // Asserting a fixed count instead would fail the moment a purely decorative one is added -
    // which is exactly what happened - while still passing if a blur escaped alongside two that
    // stayed. Other components may carry their own blurs; this guard is about <main>'s.
    expect(decoration.querySelectorAll("div.blur-3xl").length).toBeGreaterThan(0);
    expect(document.querySelectorAll("main > div.blur-3xl")).toHaveLength(0);
  });

  it("C5: the clip layer cannot intercept pointer events", () => {
    renderBuilder();
    const decoration = screen.getByTestId("plan-builder-decoration");
    expect(decoration.className).toContain("pointer-events-none");
    expect(decoration).toHaveAttribute("aria-hidden", "true");
  });

  it("H1/C1: the stepper is wrapped in a breakpoint-independent sticky box", () => {
    renderBuilder();
    const progress = screen.getByRole("region", { name: "Plan creation progress" });
    const wrapper = progress.parentElement!;
    expect(wrapper.className).toContain("sticky");
    expect(wrapper.className).toContain("top-0");
    // No responsive prefix on the sticky classes: sticking applies at every width (C1).
    expect(wrapper.className).not.toMatch(/(sm|md|lg|xl):sticky/);
  });

  it("H4/C4: the stepper sits below the z-50 Radix portals", () => {
    renderBuilder();
    const wrapper = screen
      .getByRole("region", { name: "Plan creation progress" })
      .parentElement!;
    const zClass = wrapper.className.match(/z-(\d+)/);
    expect(zClass).not.toBeNull();
    expect(Number(zClass![1])).toBeLessThan(50);
  });

  it("H2: the preview panel is sticky with a computed offset, not the old flat top-5", () => {
    renderBuilder();
    const preview = screen.getByTestId("plan-preview-sticky");
    expect(preview.className).toContain("sticky");
    expect(preview.className).not.toContain("top-5");
    // Offset comes from the measured stepper height (0 in jsdom) plus the space-y-5 rhythm.
    expect(preview.getAttribute("style")).toContain("1.25rem");
  });

  it("C1/C6: the preview aside keeps its xl-only gating", () => {
    renderBuilder();
    const aside = screen.getByLabelText("Plan preview");
    expect(aside.className).toContain("hidden");
    expect(aside.className).toContain("xl:block");
  });

  it("renders without either observer present", () => {
    // Guards the degraded path: a missing observer must not break the page.
    const io = globalThis.IntersectionObserver;
    const ro = globalThis.ResizeObserver;
    // @ts-expect-error deliberately removing the globals
    delete globalThis.IntersectionObserver;
    // @ts-expect-error deliberately removing the globals
    delete globalThis.ResizeObserver;
    try {
      expect(() => renderBuilder()).not.toThrow();
      expect(
        screen.getByRole("region", { name: "Plan creation progress" }),
      ).toBeInTheDocument();
    } finally {
      globalThis.IntersectionObserver = io;
      globalThis.ResizeObserver = ro;
    }
  });
});
