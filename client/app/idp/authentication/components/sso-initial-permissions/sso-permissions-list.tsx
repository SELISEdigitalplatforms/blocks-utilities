
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { useMemo } from "react";
import { DeleteSSOPermission } from "./delete-sso-permission";
import { IPermission } from "@blocks-idp/iam/models/permission";

interface SSOPermissionsListProps {
  permissions: IPermission[];
  onDelete: (data: IPermission) => void;
}

export const SSOPermissionsList = ({ permissions, onDelete }: SSOPermissionsListProps) => {
  const columns = useMemo<ColumnDef<IPermission>[]>(
    () => [
      {
        id: "name",
        accessorFn: (row) => `${row.name}`.trim(),
        header: () => {
          return (
            <div className="flex items-center">
              <span className="font-bold text-medium-emphasis">Name</span>
            </div>
          );
        },
        cell: (permission) => (
          <div>
            <span className="truncate rounded-sm bg-blocks-primary-shades-300 px-2 py-1">
              {permission.row.original.name}
            </span>
          </div>
        ),
      },
      {
        id: "resource",
        accessorFn: (row) => `${row.resource}`.trim(),
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Resource</span>
          </div>
        ),
        cell: (permission) => (
          <div className="flex w-[180px] items-center break-all">
            <span>{permission.row.original.resource}</span>
          </div>
        ),
      },
      {
        id: "actions",
        enableHiding: false,
        cell: ({ row }) => (
          <div
            className="flex"
            onClick={(e) => {
              e.stopPropagation();
            }}
          >
            <DeleteSSOPermission permission={row.original} onDelete={onDelete} />
          </div>
        ),
      },
    ],
    [onDelete],
  );

  const table = useReactTable({
    data: permissions,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  return (
    <Table className="text-sm">
      <TableHeader>
        {table.getHeaderGroups().map((headerGroup) => (
          <TableRow key={headerGroup.id} className="px-4 py-3 hover:bg-transparent">
            {headerGroup.headers.map((header) => (
              <TableHead key={header.id} className="font-bold text-medium-emphasis">
                {header.isPlaceholder
                  ? null
                  : flexRender(header.column.columnDef.header, header.getContext())}
              </TableHead>
            ))}
          </TableRow>
        ))}
      </TableHeader>
      <TableBody>
        {table?.getRowModel()?.rows?.length ? (
          table?.getRowModel()?.rows.map((row) => (
            <TableRow
              key={row.id}
              data-state={row.getIsSelected() && "selected"}
              className="cursor-pointer"
            >
              {row.getVisibleCells().map((cell) => (
                <TableCell key={cell.id} className="!py-4">
                  {flexRender(cell.column.columnDef.cell, cell.getContext())}
                </TableCell>
              ))}
            </TableRow>
          ))
        ) : (
          <TableRow>
            <TableCell colSpan={columns.length} className="h-24 text-center text-muted-foreground">
              No permissions found
            </TableCell>
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
};
