import React, { useCallback, useEffect, useState } from "react";
import { Table } from "@tanstack/react-table";
import { Button } from "@/components/ui-kits/button/button";
import { Cross2Icon } from "@radix-ui/react-icons";
import { DataTableFacetedFilter } from "@/components/data-table-faceted-filter/data-table-faceted-filter";
import { DateRangeFilter } from "@/components/date-range-filter/date-range-filter";
import { translation } from "@blocks-localization/models/language";
import { DateRange } from "react-day-picker";
import useIsMobile from "@/hooks/use-is-mobile";
import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui-kits/sheet/sheet";
import { Filter } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { useActiveFiltersCount } from "@/hooks/use-active-filters-count";
import { SearchInput } from "@/components/search-input/search-input";
import useIsServiceBarOpenLocal from "@blocks-localization/hooks/use-is-service-tab-open-local";

interface UsersRoleTableToolbarProps<TData> {
  table: Table<TData>;
}

export function UsersRoleTableToolbar<TData>({ table }: UsersRoleTableToolbarProps<TData>) {
  const isMobile = useIsMobile();
  const isServiceBarOpen = useIsServiceBarOpenLocal();
  const textSearchColumn = table.getColumn("name");
  const [dateRange, setDateRange] = useState<DateRange | undefined>(undefined);
  const [searchValue, setSearchValue] = useState("");
  const [isSearchVisible, setIsSearchVisible] = useState(!isMobile);

  const activeFiltersCount = useActiveFiltersCount(table, dateRange, "name");
  const isFiltered = activeFiltersCount > 0;

  useEffect(() => {
    setIsSearchVisible(!isMobile);
  }, [isMobile]);

  const onSearchInputChange = useCallback(
    (text: string) => {
      setSearchValue(text);
      textSearchColumn?.setFilterValue(text);
    },
    [textSearchColumn]
  );

  function resetFilters() {
    setSearchValue("");

    setDateRange(undefined);
    table.resetColumnFilters();
  }

  const FilterContent = () => (
    <>
      {table.getColumn("lastLogin") && (
        <DateRangeFilter
          column={table.getColumn("lastLogin")}
          title="Date added"
          date={dateRange}
          onDateChange={setDateRange}
        />
      )}
      {table.getColumn("lastLogin") && (
        <DataTableFacetedFilter column={table.getColumn("lastLogin")} title="Last login" options={translation} />
      )}
    </>
  );

  return (
    <div className="flex flex-col space-y-4 md:space-y-0">
      {/* Mobile view */}
      <div className={`flex items-center justify-between ${isServiceBarOpen ? "flex" : "hidden"}`}>
        <SearchInput
          placeholder="Filter users by name or email"
          onSearch={onSearchInputChange}
          toggleable={true}
          className="h-8 w-[250px]"
          value={searchValue}
          isVisible={isSearchVisible}
          setIsVisible={setIsSearchVisible}
        />
        {isServiceBarOpen && (
          <Sheet>
            <SheetTrigger asChild>
              <Button variant="outline" size="sm" className="relative h-8 w-8 p-0">
                <Filter className="h-4 w-4" />
                {activeFiltersCount > 0 && (
                  <Badge className="absolute -right-2 -top-2 h-4 w-4 px-1 text-xs font-medium">
                    {activeFiltersCount}
                  </Badge>
                )}
              </Button>
            </SheetTrigger>
            <SheetContent side="right" className="w-full" aria-describedby="filter-description">
              <SheetTitle className="mb-4">Filter</SheetTitle>
              <SheetDescription />
              <div className="flex flex-col space-y-4">
                <FilterContent />
                <SheetClose asChild>
                  <Button className="mt-4" size="sm">
                    Show Results
                  </Button>
                </SheetClose>
                {isFiltered && (
                  <Button variant="outline" onClick={resetFilters} className="h-8 px-2 lg:px-3">
                    Reset
                    <Cross2Icon className="ml-2 h-4 w-4" />
                  </Button>
                )}
              </div>
            </SheetContent>
          </Sheet>
        )}
      </div>

      {/* Desktop view */}
      <div className={`${isServiceBarOpen ? "hidden" : "flex"} flex-1 items-center space-x-2`}>
        <SearchInput
          placeholder="Filter users by name or email"
          onSearch={onSearchInputChange}
          className="h-8 w-[268px]"
          value={searchValue}
          isVisible={isSearchVisible}
          setIsVisible={setIsSearchVisible}
        />
        <FilterContent />
        {isFiltered && (
          <Button variant="outline" onClick={resetFilters} className="h-8 px-2 lg:px-3">
            Reset
            <Cross2Icon className="ml-2 h-4 w-4" />
          </Button>
        )}
      </div>
    </div>
  );
}
