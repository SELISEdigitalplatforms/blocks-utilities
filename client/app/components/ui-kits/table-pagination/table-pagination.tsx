import React from "react";
import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight } from "lucide-react";
import { Table } from "@tanstack/react-table";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Button } from "@/components/ui-kits/button/button";

interface TablePaginationProps<TData> {
  table: Table<TData>;
  // eslint-disable-next-line no-unused-vars
  onPageChange?: (pageIndex: number) => void;
  totalCount?: number;
}

export function TablePagination<TData>({
  table,
  onPageChange,
  totalCount,
}: TablePaginationProps<TData>) {
  const pageSize = 10;
  const totalDataLength = table.getFilteredRowModel().rows.length;
  const rowPerPage = [];

  for (let i = pageSize; i < totalDataLength + pageSize; i += pageSize) {
    rowPerPage.push(i);
  }

  return (
    <div className="flex items-center justify-between px-2 py-4">
      <div className="flex-1 pl-4 text-sm text-muted-foreground">
        {totalCount ? (
          <div>
            Total {totalCount} {totalCount > 1 ? "items" : "item"}
          </div>
        ) : (
          <div>
            {table.getFilteredSelectedRowModel().rows.length} of{" "}
            {table.getFilteredRowModel().rows.length} row(s) selected.
          </div>
        )}
      </div>
      <div className="flex items-center space-x-6 lg:space-x-8">
        <div className="flex items-center space-x-2">
          <p className="text-sm font-medium">Rows per page</p>
          <Select
            value={`${table.getState().pagination.pageSize}`}
            onValueChange={(value) => {
              table.setPageSize(Number(value));
            }}
          >
            <SelectTrigger className="h-8 w-[70px]">
              <SelectValue placeholder={table.getState().pagination.pageSize} />
            </SelectTrigger>
            <SelectContent side="top">
              {rowPerPage.map((pageSize) => (
                <SelectItem key={pageSize} value={`${pageSize}`}>
                  {pageSize}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="flex w-[100px] items-center justify-center text-sm font-medium">
          Page {table.getState().pagination.pageIndex + 1} of {table.getPageCount()}
        </div>
        <div className="flex items-center space-x-2">
          <Button
            variant="outline"
            className="flex h-8 w-8 p-0"
            onClick={() => {
              table.setPageIndex(0);
              onPageChange?.(0);
            }}
            disabled={!table.getCanPreviousPage()}
          >
            <ChevronsLeft className="h-4 w-4" />
          </Button>
          <Button
            variant="outline"
            className="h-8 w-8 p-0"
            onClick={() => {
              table.previousPage();
              onPageChange?.(table.getState().pagination.pageIndex - 1);
            }}
            disabled={!table.getCanPreviousPage()}
          >
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <Button
            variant="outline"
            className="h-8 w-8 p-0"
            onClick={() => {
              table.nextPage();
              onPageChange?.(table.getState().pagination.pageIndex + 1);
            }}
            disabled={!table.getCanNextPage()}
          >
            <ChevronRight className="h-4 w-4" />
          </Button>
          <Button
            variant="outline"
            className="flex h-8 w-8 p-0"
            onClick={() => {
              table.setPageIndex(table.getPageCount() - 1);
              onPageChange?.(table.getPageCount() - 1);
            }}
            disabled={!table.getCanNextPage()}
          >
            <ChevronsRight className="h-4 w-4" />
          </Button>
        </div>
      </div>
    </div>
  );
}

export default TablePagination;
