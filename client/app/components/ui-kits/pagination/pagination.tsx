import React from "react";
import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight } from "lucide-react";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Button } from "@/components/ui-kits/button/button";

interface PaginationProps {
  onChange: (pageIndex: number) => void;
  totalCount: number;
  pageSizeOptions?: number[];
  pageSize: number;
  page: number;
  onPageSizeChange?: (pageSize: number) => void;
}

export function Pagination({
  page,
  onChange,
  totalCount,
  pageSizeOptions,
  pageSize,
  onPageSizeChange,
}: PaginationProps) {
  const pageChangeHandler = (page: number) => {
    onChange(page);
  };

  const canGoPreviousPage = !!page;
  const totalPage = Math.ceil(totalCount / pageSize);
  const canGoNextPage = page < totalPage - 1;

  const onPageSizeChangeHandler = (value: string) => {
    if (onPageSizeChange) onPageSizeChange(+value);
  };

  return (
    <div className="flex flex-col gap-0 md:flex-row md:gap-8">
      {pageSizeOptions && pageSizeOptions?.length > 0 ? (
        <div className="flex items-center space-x-2">
          <p className="text-sm font-medium">Rows per page</p>
          <Select value={`${pageSize}`} onValueChange={onPageSizeChangeHandler}>
            <SelectTrigger className="h-8 w-[70px]">
              <SelectValue placeholder={pageSize} />
            </SelectTrigger>
            <SelectContent side="top">
              {pageSizeOptions?.map((pageSize) => (
                <SelectItem key={pageSize} value={`${pageSize}`}>
                  {pageSize}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      ) : (
        ""
      )}

      <div className="mt-2 flex items-center gap-4 md:mt-0">
        <div className="flex items-center justify-center text-sm font-medium">
          Page {page + 1} of {totalPage}
        </div>
        <div className="flex items-center gap-1">
          <Button
            variant="outline"
            className={`flex h-8 w-8 p-0 disabled:cursor-not-allowed`}
            onClick={() => {
              pageChangeHandler(0);
            }}
            disabled={!canGoPreviousPage}
          >
            <ChevronsLeft className="h-4 w-4" />
          </Button>
          <Button
            variant="outline"
            className="h-8 w-8 p-0"
            onClick={() => {
              pageChangeHandler(page - 1);
            }}
            disabled={!canGoPreviousPage}
          >
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <Button
            variant="outline"
            className="h-8 w-8 p-0"
            onClick={() => {
              pageChangeHandler(page + 1);
            }}
            disabled={!canGoNextPage}
          >
            <ChevronRight className="h-4 w-4" />
          </Button>
          <Button
            variant="outline"
            className="flex h-8 w-8 p-0"
            onClick={() => {
              pageChangeHandler(totalPage - 1);
            }}
            disabled={!canGoNextPage}
          >
            <ChevronsRight className="h-4 w-4" />
          </Button>
        </div>
      </div>
    </div>
  );
}
