import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { EMPTY_DRAFT, type CampaignDraft } from "./campaign-draft";
import { StepBenefit } from "./step-benefit";

const DURATION_LABEL = /Number of discounted billing periods \(optional\)/;

const renderStep = (overrides: Partial<CampaignDraft> = {}) => {
  const onChange = vi.fn();
  render(<StepBenefit draft={{ ...EMPTY_DRAFT, ...overrides }} onChange={onChange} />);
  return { onChange };
};

const durationInput = () => screen.getByLabelText(DURATION_LABEL);
const startInput = () => screen.getByLabelText(/Starts at \(optional\)/);
const expiryInput = () => screen.getByLabelText(/Expires at \(optional\)/);

describe("StepBenefit duration field", () => {
  it("names the field by what it counts, not by a bare 'duration'", () => {
    renderStep();

    expect(durationInput()).toBeInTheDocument();
    expect(screen.queryByLabelText(/^Duration in billing periods/)).not.toBeInTheDocument();
  });

  /**
   * The example is the whole point of the rename: "3" has to mean three charges, not three of
   * something the reader has to guess at. Tied to the input with aria-describedby so it is read
   * out rather than merely printed underneath.
   */
  it("explains what a number in it does, and announces that explanation", () => {
    renderStep();

    const help = screen.getByText(
      /Example: 3 applies the discount to the next three charges\. Leave empty for no period limit\./,
    );

    expect(help).toBeInTheDocument();
    expect(durationInput()).toHaveAttribute("aria-describedby", help.id);
    expect(help.id).toBe("campaign-duration-help");
  });

  it("still reports what was typed as durationPeriods", () => {
    const { onChange } = renderStep();

    fireEvent.change(durationInput(), { target: { value: "3" } });

    expect(onChange).toHaveBeenCalledWith({ durationPeriods: "3" });
  });

  it("shows the value it was given and stays a whole-number control", () => {
    renderStep({ durationPeriods: "6" });

    expect(durationInput()).toHaveValue(6);
    expect(durationInput()).toHaveAttribute("type", "number");
    expect(durationInput()).toHaveAttribute("min", "1");
  });
});

describe("StepBenefit availability dates", () => {
  /**
   * The layout claim, asserted on the rendered tree rather than by reading the JSX: the two dates
   * share one container, that container is the two-column responsive grid, and the duration field
   * is outside it. A future edit that puts duration back into the grid fails here.
   */
  it("puts both dates in one responsive two-column row that excludes the duration field", () => {
    renderStep();

    const row = startInput().closest("div.grid");

    expect(row).not.toBeNull();
    expect(row).toBe(expiryInput().closest("div.grid"));
    expect(row!.className).toContain("sm:grid-cols-2");
    expect(row!.contains(durationInput())).toBe(false);
  });

  /** Below sm there is one column, so the pair stacks — a datetime-local control needs the width. */
  it("declares no column count of its own, so the pair stacks on narrow screens", () => {
    renderStep();

    const row = startInput().closest("div.grid")!;

    expect(row.className).not.toMatch(/(^|\s)grid-cols-/);
  });

  it("keeps both labels, both help texts, and both reported values", () => {
    const { onChange } = renderStep({
      startsAtUtc: "2026-10-01T09:30",
      expiresAtUtc: "2026-10-31T18:00",
    });

    expect(startInput()).toHaveValue("2026-10-01T09:30");
    expect(expiryInput()).toHaveValue("2026-10-31T18:00");
    expect(
      screen.getByText(/Leave empty to make the code available immediately\./),
    ).toBeInTheDocument();
    expect(screen.getByText(/The code is unavailable at and after this instant\./)).toBeInTheDocument();

    fireEvent.change(startInput(), { target: { value: "2026-11-01T00:00" } });
    expect(onChange).toHaveBeenCalledWith({ startsAtUtc: "2026-11-01T00:00" });

    fireEvent.change(expiryInput(), { target: { value: "2026-11-30T23:59" } });
    expect(onChange).toHaveBeenCalledWith({ expiresAtUtc: "2026-11-30T23:59" });
  });
});

/**
 * The relayout lives inside the existing Standard-only branch. Campaign kinds set their own
 * duration and validity server-side and must not gain these controls.
 */
describe("StepBenefit on campaign kinds", () => {
  it.each(["FirstAnnualPeriod", "FreeOpeningCalendarPeriod"] as const)(
    "offers neither a duration nor availability dates for %s",
    (campaignKind) => {
      renderStep({ campaignKind });

      expect(screen.queryByLabelText(DURATION_LABEL)).not.toBeInTheDocument();
      expect(screen.queryByLabelText(/Starts at \(optional\)/)).not.toBeInTheDocument();
      expect(screen.queryByLabelText(/Expires at \(optional\)/)).not.toBeInTheDocument();
    },
  );
});
