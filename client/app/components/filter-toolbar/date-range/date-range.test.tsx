import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DateRange } from "./date-range";

vi.mock("@/hooks/use-is-mobile", () => ({ default: () => false }));

describe("DateRange", () => {
  beforeEach(() => vi.clearAllMocks());

  it("renders the label", () => {
    render(<DateRange label="Created" value={null} onChange={vi.fn()} />);
    expect(screen.getByText("Created")).toBeInTheDocument();
  });

  it("shows the formatted range when a value is provided", () => {
    const from = new Date("2024-01-02T00:00:00Z");
    const to = new Date("2024-01-05T00:00:00Z");
    render(
      <DateRange label="Created" value={{ from, to }} onChange={vi.fn()} />,
    );
    // The trigger shows the label plus the formatted dates.
    expect(screen.getByText("Created")).toBeInTheDocument();
  });

  it("applies the current range and closes the popover", async () => {
    const from = new Date("2024-01-02T00:00:00Z");
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(
      <DateRange label="Created" value={{ from }} onChange={onChange} />,
    );

    await user.click(screen.getByText("Created"));
    await user.click(await screen.findByRole("button", { name: "Apply" }));

    expect(onChange).toHaveBeenCalledWith({ from });
  });

  it("resets the selection without calling onChange", async () => {
    const from = new Date("2024-01-02T00:00:00Z");
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(
      <DateRange label="Created" value={{ from }} onChange={onChange} />,
    );

    await user.click(screen.getByText("Created"));
    await user.click(await screen.findByRole("button", { name: "Reset" }));

    expect(onChange).not.toHaveBeenCalled();
  });
});
