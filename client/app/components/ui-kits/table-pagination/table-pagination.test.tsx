import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import type { Table } from "@tanstack/react-table";
import { TablePagination } from "./table-pagination";

const makeTable = (over: Partial<{ pageIndex: number; canPrev: boolean; canNext: boolean }> = {}) => {
  const pageIndex = over.pageIndex ?? 0;
  const fns = {
    setPageIndex: vi.fn(),
    previousPage: vi.fn(),
    nextPage: vi.fn(),
    setPageSize: vi.fn(),
  };
  const table = {
    ...fns,
    getFilteredRowModel: () => ({ rows: new Array(25).fill(0) }),
    getFilteredSelectedRowModel: () => ({ rows: new Array(2).fill(0) }),
    getState: () => ({ pagination: { pageSize: 10, pageIndex } }),
    getPageCount: () => 3,
    getCanPreviousPage: () => over.canPrev ?? true,
    getCanNextPage: () => over.canNext ?? true,
  };
  return { table: table as unknown as Table<unknown>, fns };
};

describe("TablePagination", () => {
  it("shows the selected-row summary when no totalCount is given", () => {
    const { table } = makeTable();
    render(<TablePagination table={table} />);
    expect(screen.getByText(/2 of 25 row\(s\) selected/)).toBeInTheDocument();
  });

  it("shows a singular total item label", () => {
    const { table } = makeTable();
    render(<TablePagination table={table} totalCount={1} />);
    expect(screen.getByText(/Total 1 item/)).toBeInTheDocument();
  });

  it("navigates via the paging buttons", () => {
    const onPageChange = vi.fn();
    const { table, fns } = makeTable({ pageIndex: 1 });
    render(<TablePagination table={table} onPageChange={onPageChange} />);
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[0]);
    fireEvent.click(buttons[3]);
    expect(fns.setPageIndex).toHaveBeenCalledWith(0);
    expect(fns.setPageIndex).toHaveBeenCalledWith(2);
    expect(onPageChange).toHaveBeenCalled();
  });
});
