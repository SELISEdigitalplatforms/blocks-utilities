import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DropdownSearchInput } from "./dropdown-search-input";

const options = [
  { label: "Name", value: "name" },
  { label: "Email", value: "email" },
];

describe("DropdownSearchInput", () => {
  beforeEach(() => vi.clearAllMocks());

  it("renders the input with the placeholder", () => {
    render(
      <DropdownSearchInput
        onChange={vi.fn()}
        value={{ selected: "name", value: "" }}
        options={options}
        placeholder="Find..."
      />,
    );
    expect(screen.getByPlaceholderText("Find...")).toBeInTheDocument();
  });

  it("debounces the onChange while typing", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(
      <DropdownSearchInput
        onChange={onChange}
        value={{ selected: "name", value: "" }}
        options={options}
      />,
    );

    await user.type(screen.getByPlaceholderText("Search..."), "ab");
    await waitFor(
      () =>
        expect(onChange).toHaveBeenCalledWith({ selected: "name", value: "ab" }),
      { timeout: 1000 },
    );
  });

  it("clears the value immediately when the clear button is clicked", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(
      <DropdownSearchInput
        onChange={onChange}
        value={{ selected: "name", value: "abc" }}
        options={options}
      />,
    );

    // The clear button is the last button (the select trigger is the first).
    const buttons = screen.getAllByRole("button");
    await user.click(buttons[buttons.length - 1]);
    expect(onChange).toHaveBeenCalledWith({ selected: "name", value: "" });
  });
});
