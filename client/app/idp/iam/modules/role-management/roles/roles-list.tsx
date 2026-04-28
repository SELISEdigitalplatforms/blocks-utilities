import { Button } from "@/components/ui-kits/button/button";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { Pencil } from "lucide-react";
import { useCallback, useMemo, useState } from "react";
import { UpdateRole } from "../update-role/update-role";
import { useNavigate } from "react-router-dom";
import { IRole } from "@blocks-idp/iam/models/role";
import { FilterControls, SortValue } from "@/components/filter-toolbar";
import { useRolesSortQueryParams } from "./roles-filter-toolbar";

type RolesTableProps = {
  roles: IRole[];
  isLoading: boolean;
};

const LoadingSkelton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 5 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

export const RolesList = ({ roles, isLoading }: RolesTableProps) => {
  const { sortQueryParams, setSortQueryParams } = useRolesSortQueryParams();
  const [selectedRole, setSelectedRole] = useState<IRole | null>(null);
  const navigate = useNavigate();

  const sortHandler = useCallback(
    (value: SortValue) => {
      setSortQueryParams(value);
    },
    [setSortQueryParams],
  );

  const columns = useMemo<ColumnDef<IRole>[]>(
    () => [
      {
        id: "name",
        accessorFn: (row) => `${row.name}`.trim(),
        header: () => (
          <FilterControls.SortHeader
            id="Name"
            label="Name"
            value={sortQueryParams}
            onChange={sortHandler}
          />
        ),
        cell: (roles) => <div className="w-[130px] truncate">{roles.row.original.name}</div>,
      },
      {
        id: "slug",
        accessorFn: (row) => `${row.slug}`.trim(),
        header: () => (
          <FilterControls.SortHeader
            id="Slug"
            label="Slug"
            value={sortQueryParams}
            onChange={sortHandler}
          />
        ),
        cell: (roles) => (
          <div className="w-[150px] truncate">
            <span className="rounded-sm bg-blocks-primary-shades-300 px-2 py-1">
              {roles.row.original.slug}
            </span>
          </div>
        ),
      },
      {
        id: "count",
        accessorFn: (row) => `${row.count}`.trim(),
        header: () => (
          <FilterControls.SortHeader
            id="Count"
            label="Permissions"
            value={sortQueryParams}
            onChange={sortHandler}
          />
        ),
        cell: (roles) => <div className="w-[180px] truncate">{roles.row.original.count}</div>,
      },
      {
        id: "description",
        accessorFn: (row) => `${row.description}`.trim(),
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Description</span>
          </div>
        ),
        cell: (roles) => (
          <div className="w-[200px] truncate md:w-[260px]">{roles.row.original.description}</div>
        ),
      },
      {
        id: "actions",
        enableHiding: false,
        cell: ({ row }) => (
          <div className="flex">
            <Button
              size="icon"
              className="rounded-full"
              variant="ghost"
              onClick={(event) => {
                event.stopPropagation();
                setSelectedRole(() => row.original);
              }}
            >
              <Pencil className="h-4 w-4" />
            </Button>
          </div>
        ),
      },
    ],
    [sortHandler, sortQueryParams],
  );

  const table = useReactTable({
    data: roles,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  const onRowClickHandler = (itemId: number | string) => {
    navigate(`/services/iam/role-detail/${itemId}`);
  };

  if (isLoading) return <LoadingSkelton />;

  return (
    <>
      <Table>
        <TableHeader>
          <TableRow className="px-4 py-3 hover:bg-transparent">
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
          {!roles.length ? (
            <TableRow>
              <TableCell
                colSpan={columns.length}
                className="h-24 text-center text-muted-foreground"
              >
                No roles found. Please create new roles.
              </TableCell>
            </TableRow>
          ) : (
            table.getRowModel().rows.map((row) => (
              <TableRow
                key={row.id}
                className="cursor-pointer"
                onClick={() => onRowClickHandler(row.original.itemId)}
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
      {selectedRole && (
        <Dialog
          open={!!selectedRole}
          onOpenChange={(value) => {
            if (!value) setSelectedRole(null);
          }}
        >
          <UpdateRole
            role={selectedRole}
            isOpen={!!selectedRole}
            onClose={() => setSelectedRole(null)}
          />
        </Dialog>
      )}
    </>
  );
};
