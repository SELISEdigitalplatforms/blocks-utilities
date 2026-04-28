import { useMemo } from "react";
import { Table } from "@tanstack/react-table";
import { DateRange } from "react-day-picker";

export const useActiveFiltersCount = <TData,>(
  table: Table<TData>,
  dateRange: DateRange | undefined,
  searchColumnId: string | undefined,
) => {
  return useMemo(() => {
    let count = 0;
    table.getState().columnFilters.forEach((filter) => {
      if (filter.id === searchColumnId) {
        if (typeof filter.value === "object" && filter.value !== null && "types" in filter.value) {
          count += (filter.value.types as string[]).length;
        }
      } else if (typeof filter.value === "object" && filter.value !== null) {
        if (Array.isArray(filter.value)) {
          count += filter.value.length;
        } else {
          count += Object.keys(filter.value).length;
        }
      } else if (filter.value !== undefined && filter.value !== "") {
        count += 1;
      }
    });

    if (dateRange?.from || dateRange?.to) {
      count += 1;
    }

    return count;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [table.getState().columnFilters, dateRange]);
};
