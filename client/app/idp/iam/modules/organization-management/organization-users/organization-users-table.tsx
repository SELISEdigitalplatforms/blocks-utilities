import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { checkValidDate, formatDate, parseDateString } from "@/lib/utils";
import { User } from "@blocks-idp/iam/models/user";
import {
  ColumnDef,
  flexRender,
  getCoreRowModel,
  getFacetedRowModel,
  getFacetedUniqueValues,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable,
} from "@tanstack/react-table";
import { useMemo } from "react";
import { useOrganizationUsersSortQueryParams } from "./organization-users-filter-toolbar";
import { FilterControls } from "@/components/filter-toolbar";
import { useNavigate } from "react-router-dom";

type OrganizationUsersTableProps = {
  users: User[];
  isLoading: boolean;
};

const LoadingSkelton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 10 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-lg" />
    ))}
  </div>
);

export const OrganizationUsersTable = ({ users, isLoading }: OrganizationUsersTableProps) => {
  const navigate = useNavigate();
  const { sortQueryParams, setSortQueryParams } = useOrganizationUsersSortQueryParams();

  const columns = useMemo<ColumnDef<User>[]>(
    () => [
      {
        id: "name",
        accessorFn: (row) => `${row.firstName} ${row.lastName || ""}`.trim(),
        header: () => (
          <FilterControls.SortHeader
            id="FirstName"
            label="Name"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: (info) => (
          <div className="ml-2 w-[180px] truncate sm:ml-0 md:w-[240px]">
            {`${info.row.original.firstName || ""} ${info.row.original.lastName || ""}`.trim() ||
              "-"}
          </div>
        ),
      },
      {
        id: "email",
        accessorFn: (row) => row.email,
        header: () => (
          <FilterControls.SortHeader
            id="Email"
            label="Email"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: (info) => (
          <div className="ml-2 flex w-[250px] items-center gap-2 truncate lowercase sm:ml-0 md:w-[300px]">
            <CopyToClipboardButton textToCopy={info.row.original.email} isHoverable>
              {info.row.original.email || "-"}
            </CopyToClipboardButton>
          </div>
        ),
      },
      {
        accessorKey: "logInCount",
        header: () => (
          <FilterControls.SortHeader
            id="LogInCount"
            label="No. of logins"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: ({ row }) => {
          return (
            <div className="ml-2 w-[180px] text-center lowercase sm:ml-0">
              {row.getValue("logInCount")}
            </div>
          );
        },
      },
      {
        accessorKey: "lastLoggedInTime",
        header: () => (
          <FilterControls.SortHeader
            id="LastLoggedInTime"
            label="Last login"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: ({ row }) => {
          return (
            <div className="ml-2 w-[180px] lowercase sm:ml-0">
              {checkValidDate(row.getValue("lastLoggedInTime"))
                ? formatDate(parseDateString(row.getValue("lastLoggedInTime")))
                : "-"}
            </div>
          );
        },
      },
      {
        id: "status",
        accessorFn: (row) => row.active,
        header: () => (
          <FilterControls.SortHeader
            id="Active"
            label="Status"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: (info) => (
          <Badge variant={info.row.original.active ? "success" : "error"} className="w-fit">
            {info.row.original.active ? "Active" : "Inactive"}
          </Badge>
        ),
      },
    ],
    [setSortQueryParams, sortQueryParams],
  );

  const handleRowClick = (itemId: string) => {
    navigate(`/services/iam/user-detail/${itemId}`);
  };
  const table = useReactTable({
    data: users,
    columns,
    enableRowSelection: true,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFacetedRowModel: getFacetedRowModel(),
    getFacetedUniqueValues: getFacetedUniqueValues(),
  });

  if (isLoading) return <LoadingSkelton />;

  return (
    <Table>
      <TableHeader>
        <TableRow isHoverable>
          {table
            .getHeaderGroups()
            .map((headerGroup) =>
              headerGroup.headers.map((header) => (
                <TableHead key={header.id}>
                  {header.isPlaceholder
                    ? null
                    : flexRender(header.column.columnDef.header, header.getContext())}
                </TableHead>
              )),
            )}
        </TableRow>
      </TableHeader>
      <TableBody>
        {!users.length ? (
          <TableRow>
            <TableCell colSpan={columns.length} className="h-24 text-center text-muted-foreground">
              No results found.
            </TableCell>
          </TableRow>
        ) : (
          table.getRowModel().rows.map((row) => (
            <TableRow
              key={row.id}
              className="cursor-pointer"
              onClick={() => handleRowClick(row.original.itemId)}
              isHoverable
            >
              {row.getVisibleCells().map((cell) => (
                <TableCell key={cell.id}>
                  {flexRender(cell.column.columnDef.cell, cell.getContext())}
                </TableCell>
              ))}
            </TableRow>
          ))
        )}
      </TableBody>
    </Table>
  );
};
