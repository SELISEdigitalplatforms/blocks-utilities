import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MultiSelect } from "./multi-select";

vi.mock("@/hooks/use-is-mobile", () => ({ default: () => false }));

const options = [
  { label: "Active", value: "active" },
  { label: "Disabled", value: "disabled" },
];

describe("MultiSelect", () => {
  beforeEach(() => vi.clearAllMocks());

  it("renders the label", () => {
    render(
      <MultiSelect label="Status" options={options} value={[]} onChange={vi.fn()} />,
    );
    expect(screen.getAllByText("Status").length).toBeGreaterThan(0);
  });

  it("shows badges for the selected values", () => {
    render(
      <MultiSelect
        label="Status"
        options={options}
        value={["active"]}
        onChange={vi.fn()}
      />,
    );
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("adds a value when an unselected option is chosen", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(
      <MultiSelect
        label="Status"
        options={options}
        value={[]}
        onChange={onChange}
      />,
    );

    await user.click(screen.getAllByText("Status")[0]);
    await user.click(await screen.findByText("Disabled"));
    expect(onChange).toHaveBeenCalledWith(["disabled"]);
  });

  it("clears all values via the Clear action", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(
      <MultiSelect
        label="Status"
        options={options}
        value={["active"]}
        onChange={onChange}
      />,
    );

    await user.click(screen.getAllByText("Status")[0]);
    await user.click(await screen.findByText("Clear"));
    expect(onChange).toHaveBeenCalledWith([]);
  });
});
