import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DataTableFacetedFilter } from "./data-table-faceted-filter";

vi.mock("@/hooks/use-is-mobile", () => ({ default: () => false }));

const options = [
  { label: "Active", value: "active" },
  { label: "Disabled", value: "disabled" },
];

function makeColumn(overrides: Record<string, unknown> = {}) {
  return {
    getFacetedUniqueValues: () => new Map([["active", 3]]),
    getFilterValue: () => undefined,
    setFilterValue: vi.fn(),
    ...overrides,
  } as never;
}

describe("DataTableFacetedFilter", () => {
  beforeEach(() => vi.clearAllMocks());

  it("renders the title on the trigger", () => {
    render(
      <DataTableFacetedFilter
        title="Status"
        options={options}
        column={makeColumn()}
      />,
    );
    expect(screen.getAllByText("Status").length).toBeGreaterThan(0);
  });

  it("shows badges for the selected values", () => {
    const column = makeColumn({
      getFilterValue: () => ({ types: ["active"] }),
    });
    render(
      <DataTableFacetedFilter
        title="Status"
        options={options}
        column={column}
      />,
    );
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("sets the filter value when an option is selected", async () => {
    const setFilterValue = vi.fn();
    const column = makeColumn({ setFilterValue });
    const user = userEvent.setup();
    render(
      <DataTableFacetedFilter
        title="Status"
        options={options}
        column={column}
      />,
    );

    await user.click(screen.getAllByText("Status")[0]);
    await user.click(await screen.findByText("Disabled"));

    expect(setFilterValue).toHaveBeenCalledWith({ types: ["disabled"] });
  });
});
