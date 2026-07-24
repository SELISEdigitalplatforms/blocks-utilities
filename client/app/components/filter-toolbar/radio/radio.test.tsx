import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Radio } from "./radio";

vi.mock("@/hooks/use-is-mobile", () => ({ default: () => false }));

const options = [
  { label: "Active", value: "active" },
  { label: "Disabled", value: "disabled" },
];

describe("Radio", () => {
  beforeEach(() => vi.clearAllMocks());

  it("renders the label on the trigger", () => {
    render(
      <Radio label="Status" options={options} value="" onChange={vi.fn()} />,
    );
    expect(screen.getAllByText("Status").length).toBeGreaterThan(0);
  });

  it("shows the selected option as a badge", () => {
    render(
      <Radio
        label="Status"
        options={options}
        value="active"
        onChange={vi.fn()}
      />,
    );
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("calls onChange when an option is picked", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(
      <Radio label="Status" options={options} value="" onChange={onChange} />,
    );

    await user.click(screen.getAllByText("Status")[0]);
    await user.click(await screen.findByText("Disabled"));
    expect(onChange).toHaveBeenCalledWith("disabled");
  });

  it("filters options by the search box", async () => {
    const user = userEvent.setup();
    render(
      <Radio label="Status" options={options} value="" onChange={vi.fn()} />,
    );

    await user.click(screen.getAllByText("Status")[0]);
    const search = await screen.findByPlaceholderText("Status");
    await user.type(search, "dis");
    expect(screen.getByText("Disabled")).toBeInTheDocument();
    expect(screen.queryByText("Active")).not.toBeInTheDocument();
  });
});
