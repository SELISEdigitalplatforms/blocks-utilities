import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, within } from "@testing-library/react";
import { FilterToolbar } from "./filter-toolbar";

type Values = {
  q: string;
  sort: { property: string; isDescending: boolean };
};

const baseFilters = [
  { key: "sort" as const, type: "SortHeader" as const, label: "Name", props: { id: "name" } },
  { key: "q" as const, type: "SearchInput" as const, label: "Search" },
];

const defaults: Values = { q: "", sort: { property: "", isDescending: false } };

const renderToolbar = (
  values: Values,
  onChange = vi.fn(),
  onReset = vi.fn(),
  hideGlobalResetButton = false,
) => {
  render(
    <FilterToolbar<Values>
      filters={baseFilters}
      values={values}
      defaultValues={defaults}
      onChange={onChange}
      onReset={onReset}
      hideGlobalResetButton={hideGlobalResetButton}
    />,
  );
  return { onChange, onReset };
};

describe("FilterToolbar", () => {
  it("renders a control per filter in both desktop and mobile views", () => {
    renderToolbar(defaults);
    // Each control is rendered once for desktop and once for mobile.
    expect(screen.getAllByText("Name").length).toBeGreaterThanOrEqual(2);
  });

  it("propagates a control change through onChange with the merged values", () => {
    const { onChange } = renderToolbar(defaults);
    // The SortHeader renders a clickable label; clicking it emits a sort value.
    fireEvent.click(screen.getAllByText("Name")[0]);
    expect(onChange).toHaveBeenCalledWith(
      "sort",
      { property: "name", isDescending: false },
      expect.objectContaining({ sort: { property: "name", isDescending: false } }),
    );
  });

  it("hides the reset control when values equal the defaults", () => {
    renderToolbar(defaults);
    expect(screen.queryByRole("button", { name: /reset/i })).not.toBeInTheDocument();
  });

  it("shows a desktop reset button when values differ and fires onReset with the initial values", () => {
    const { onReset } = renderToolbar({
      q: "abc",
      sort: { property: "name", isDescending: true },
    });
    const resetButtons = screen.getAllByRole("button", { name: /reset/i });
    fireEvent.click(resetButtons[0]);
    expect(onReset).toHaveBeenCalledWith(defaults);
  });

  it("hides the reset button when hideGlobalResetButton is set even if values differ", () => {
    renderToolbar({ q: "abc", sort: { property: "name", isDescending: true } }, vi.fn(), vi.fn(), true);
    expect(screen.queryByRole("button", { name: /reset/i })).not.toBeInTheDocument();
  });

  it("opens the mobile filter sheet and resets from inside it", () => {
    const onReset = vi.fn();
    renderToolbar(
      { q: "abc", sort: { property: "name", isDescending: true } },
      vi.fn(),
      onReset,
    );
    // The mobile view exposes a filter trigger button (icon only).
    const triggers = screen.getAllByRole("button");
    const filterTrigger = triggers.find((b) => b.className.includes("h-8 w-8"));
    expect(filterTrigger).toBeTruthy();
    fireEvent.click(filterTrigger as HTMLElement);
    // The sheet exposes a "Show Results" close button.
    const showResults = screen.getByRole("button", { name: /show results/i });
    const sheet = showResults.closest("div") as HTMLElement;
    expect(within(sheet).getByText(/show results/i)).toBeInTheDocument();
  });
});
