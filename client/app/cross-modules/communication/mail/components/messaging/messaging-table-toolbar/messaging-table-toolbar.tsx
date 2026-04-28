

import React, { useCallback, useEffect, useState } from "react";
import { Cross2Icon } from "@radix-ui/react-icons";
import { Table } from "@tanstack/react-table";
import { Filter } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { configurations, protocols } from "@blocks-communication/mail/constants/messaging";
import { DataTableFacetedFilter } from "@/components/data-table-faceted-filter/data-table-faceted-filter";
import useIsMobile from "@/hooks/use-is-mobile";
import { useActiveFiltersCount } from "@/hooks/use-active-filters-count";
import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui-kits/sheet/sheet";
import { Badge } from "@/components/ui-kits/badge/badge";
import { SearchInput } from "@/components/search-input/search-input";
import useIsServiceBarOpenComm from "@blocks-communication/mail/hooks/use-is-service-tab-open-comm";

interface MessagingTableToolbarProps<TData> {
  table: Table<TData>;
}

export function MessagingTableToolbar<TData>({ table }: MessagingTableToolbarProps<TData>) {
  const isMobile = useIsMobile();
  const isServiceBarOpen = useIsServiceBarOpenComm();

  const textSearchColumn = table.getColumn("name");

  const [searchValue, setSearchValue] = useState("");
  const [isSearchVisible, setIsSearchVisible] = useState(!isMobile);

  const activeFiltersCount = useActiveFiltersCount(table, undefined, "name");
  const isFiltered = activeFiltersCount > 0;

  useEffect(() => {
    setIsSearchVisible(!isMobile);
  }, [isMobile]);

  const onSearchInputChange = useCallback(
    (text: string) => {
      setSearchValue(text);
      textSearchColumn?.setFilterValue(text);
    },
    [textSearchColumn],
  );

  function resetFilters() {
    setSearchValue("");
    table.resetColumnFilters();
  }

  const FilterContent = () => (
    <>
      {table.getColumn("configuration") && (
        <DataTableFacetedFilter
          column={table.getColumn("configuration")}
          title="Configuration"
          options={configurations}
        />
      )}
      {table.getColumn("protocol") && (
        <DataTableFacetedFilter
          column={table.getColumn("protocol")}
          title="Protocol"
          options={protocols}
        />
      )}
    </>
  );

  return (
    <div className="flex flex-col space-y-4 md:space-y-0">
      {/* Mobile view */}
      <div className={`flex items-center justify-between ${isServiceBarOpen ? "flex" : "hidden"}`}>
        <SearchInput
          placeholder="Filter campaigns"
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
              <SheetDescription></SheetDescription>
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
          placeholder="Filter campaigns"
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
