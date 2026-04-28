

import React, { useMemo } from "react";
import { ColumnDef, getCoreRowModel, useReactTable, flexRender } from "@tanstack/react-table";
import { Eye, MoreVertical } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { formatDate } from "@/lib/utils";
import { useGetEmailUsage } from "@blocks-communication/mail/hooks/use-email-usage";
import { StatusBadge } from "@blocks-communication/mail/email/email-usage/status-badge";
import { IEmailUsage } from "@blocks-communication/mail/models/email";
import {
  EmailUsageFilterToolbar,
  useEmailUsageFilterQueryParams,
} from "@blocks-communication/mail/email/email-usage/email-usage-filter-toolbar";
import { Link } from "react-router-dom";

const LoadingSkeleton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 5 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

export const EmailUsageList = ({ isInbound }: { isInbound: boolean }) => {
  const { queryParams, setQueryParams } = useEmailUsageFilterQueryParams();
  const { page, pageSize, search, status, startDate, endDate } = queryParams;

  const { data, isLoading } = useGetEmailUsage(
    page,
    pageSize,
    isInbound,
    search,
    status,
    startDate,
    endDate,
  );

  const columns = useMemo<ColumnDef<IEmailUsage>[]>(() => {
    const allColumns: ColumnDef<IEmailUsage>[] = [
      {
        accessorKey: "from",
        header: "From",
        cell: ({ row }) => <div className="truncate">{row.getValue("from") || "-"}</div>,
      },
      {
        accessorKey: "to",
        header: "To",
        cell: ({ row }) => <div className="truncate">{row.getValue("to") || "-"}</div>,
      },
      {
        accessorKey: "subject",
        header: "Subject",
        cell: ({ row }) => <div className="truncate">{row.getValue("subject") || "-"}</div>,
      },
      {
        accessorKey: "status",
        header: "Status",
        cell: ({ row }) => <StatusBadge status={row.getValue("status")} />,
      },
      {
        accessorKey: "date",
        header: isInbound ? "Received Date" : "Send Date",
        cell: ({ row }) => {
          const dateValue = row.getValue("date");
          if (!dateValue) return "-";
          return formatDate(new Date(dateValue as string));
        },
      },
      {
        id: "actions",
        cell: ({ row }) => {
          return (
            <div className="flex justify-end">
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button variant="ghost" className="h-8 w-8 p-0">
                    <span className="sr-only">Open menu</span>
                    <MoreVertical className="h-4 w-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem
                    className="cursor-pointer"
                    onClick={(e) => {
                      e.stopPropagation();
                    }}
                  >
                    <Link
                      to={`/utilities/email/usage/${row.original.messageId}`}
                      className="flex items-center"
                    >
                      <Eye className="mr-2 h-4 w-4" />
                      View Details
                    </Link>
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </div>
          );
        },
      },
    ];

    if (isInbound) {
      return allColumns.filter((col) => col.header !== "Status");
    }
    return allColumns;
  }, [isInbound]);

  const table = useReactTable({
    data: data?.data || [],
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  return (
    <div className="flex flex-col gap-4">
      <EmailUsageFilterToolbar isInbound={isInbound} />
      {isLoading ? (
        <LoadingSkeleton />
      ) : (
        <>
          <div>
            <Table>
              <TableHeader>
                {table.getHeaderGroups().map((headerGroup) => (
                  <TableRow key={headerGroup.id}>
                    {headerGroup.headers.map((header) => {
                      return (
                        <TableHead
                          key={header.id}
                          className={header.id === "actions" ? "w-10" : ""}
                        >
                          {header.isPlaceholder
                            ? null
                            : flexRender(header.column.columnDef.header, header.getContext())}
                        </TableHead>
                      );
                    })}
                  </TableRow>
                ))}
              </TableHeader>
              <TableBody>
                {table.getRowModel().rows?.length ? (
                  table.getRowModel().rows.map((row) => (
                    <Link
                      key={row.id}
                      to={`/utilities/email/usage/${row.original.messageId}`}
                     
                    >
                      <TableRow data-state={row.getIsSelected() && "selected"} isHoverable>
                        {row.getVisibleCells().map((cell) => (
                          <TableCell
                            key={cell.id}
                            className={cell.column.id === "actions" ? "text-right" : ""}
                          >
                            {flexRender(cell.column.columnDef.cell, cell.getContext())}
                          </TableCell>
                        ))}
                      </TableRow>
                    </Link>
                  ))
                ) : (
                  <TableRow>
                    <TableCell colSpan={columns.length} className="h-24 text-center">
                      No results.
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </div>
          {data && data.totalCount > pageSize && (
            <div className="flex items-center justify-end">
              <Pagination
                page={page}
                pageSize={pageSize}
                totalCount={data.totalCount}
                pageSizeOptions={[pageSize]}
                onChange={(page) => setQueryParams({ page })}
              />
            </div>
          )}
        </>
      )}
    </div>
  );
};
