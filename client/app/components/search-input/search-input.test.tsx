import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SearchInput } from "./search-input";

describe("SearchInput", () => {
  it("calls onSearch as the user types", async () => {
    const onSearch = vi.fn();
    const user = userEvent.setup();
    render(
      <SearchInput
        onSearch={onSearch}
        value=""
        isVisible
        setIsVisible={vi.fn()}
      />,
    );
    await user.type(screen.getByPlaceholderText("Search..."), "a");
    expect(onSearch).toHaveBeenCalledWith("a");
  });

  it("shows a clear button only when there is a value and clears on click", async () => {
    const onSearch = vi.fn();
    const setIsVisible = vi.fn();
    const user = userEvent.setup();
    const { rerender } = render(
      <SearchInput
        onSearch={onSearch}
        value=""
        isVisible
        setIsVisible={setIsVisible}
      />,
    );
    // No clear button while empty.
    expect(screen.queryAllByRole("button")).toHaveLength(0);

    rerender(
      <SearchInput
        onSearch={onSearch}
        value="hello"
        isVisible
        setIsVisible={setIsVisible}
      />,
    );
    await user.click(screen.getByRole("button"));
    expect(onSearch).toHaveBeenCalledWith("");
    // Not toggleable, so visibility is not changed.
    expect(setIsVisible).not.toHaveBeenCalled();
  });

  it("renders a toggle button when collapsed and toggleable", async () => {
    const setIsVisible = vi.fn();
    const user = userEvent.setup();
    render(
      <SearchInput
        onSearch={vi.fn()}
        value=""
        toggleable
        isVisible={false}
        setIsVisible={setIsVisible}
      />,
    );
    // Only the toggle button is shown, no input.
    expect(screen.queryByPlaceholderText("Search...")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button"));
    expect(setIsVisible).toHaveBeenCalledWith(true);
  });

  it("hides itself when cleared while toggleable", async () => {
    const onSearch = vi.fn();
    const setIsVisible = vi.fn();
    const user = userEvent.setup();
    render(
      <SearchInput
        onSearch={onSearch}
        value="x"
        toggleable
        isVisible
        setIsVisible={setIsVisible}
      />,
    );
    await user.click(screen.getByRole("button"));
    expect(onSearch).toHaveBeenCalledWith("");
    expect(setIsVisible).toHaveBeenCalledWith(false);
  });
});
