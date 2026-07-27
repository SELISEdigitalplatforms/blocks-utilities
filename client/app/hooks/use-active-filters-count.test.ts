import { renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { Table } from "@tanstack/react-table";
import type { DateRange } from "react-day-picker";
import { useActiveFiltersCount } from "./use-active-filters-count";

const makeTable = (columnFilters: { id: string; value: unknown }[]) =>
  ({
    getState: () => ({ columnFilters }),
  }) as unknown as Table<unknown>;

describe("useActiveFiltersCount", () => {
  it("counts the search column types entry", () => {
    const table = makeTable([
      { id: "search", value: { types: ["a", "b"] } },
    ]);
    const { result } = renderHook(() =>
      useActiveFiltersCount(table, undefined, "search"),
    );
    expect(result.current).toBe(2);
  });

  it("counts array filter values by length", () => {
    const table = makeTable([{ id: "status", value: ["x", "y", "z"] }]);
    const { result } = renderHook(() =>
      useActiveFiltersCount(table, undefined, "search"),
    );
    expect(result.current).toBe(3);
  });

  it("counts object filter values by key count", () => {
    const table = makeTable([{ id: "role", value: { a: 1, b: 2 } }]);
    const { result } = renderHook(() =>
      useActiveFiltersCount(table, undefined, "search"),
    );
    expect(result.current).toBe(2);
  });

  it("counts scalar non-empty values as one", () => {
    const table = makeTable([
      { id: "name", value: "abc" },
      { id: "empty", value: "" },
    ]);
    const { result } = renderHook(() =>
      useActiveFiltersCount(table, undefined, "search"),
    );
    expect(result.current).toBe(1);
  });

  it("adds one when a date range is present", () => {
    const table = makeTable([]);
    const dateRange = { from: new Date() } as DateRange;
    const { result } = renderHook(() =>
      useActiveFiltersCount(table, dateRange, "search"),
    );
    expect(result.current).toBe(1);
  });
});
