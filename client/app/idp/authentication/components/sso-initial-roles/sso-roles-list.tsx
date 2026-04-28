import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { IRole } from "@blocks-idp/iam/models/role";
import { Badge } from "@/components/ui-kits/badge/badge";
import { DeleteSSORole } from "./delete-sso-role";

type SSORolesListProps = {
  roles: IRole[];
  onDelete: (role: IRole) => void;
};

export const SSORolesList = ({ roles, onDelete }: SSORolesListProps) => {
  const navigate = useNavigate();
  const columns = useMemo<ColumnDef<IRole>[]>(
    () => [
      {
        id: "name",
        accessorFn: (row) => `${row.name}`.trim(),
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Roles</span>
          </div>
        ),
        cell: (roles) => <div className="w-[130px] truncate">{roles.row.original.name}</div>,
      },
      {
        id: "slug",
        accessorFn: (row) => `${row.slug}`.trim(),
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Slug</span>
          </div>
        ),
        cell: ({ row }) => (
          <Badge className="w-fit" variant="secondary">
            {row.original.slug}
          </Badge>
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
            <DeleteSSORole role={row.original} onDelete={onDelete} />
          </div>
        ),
      },
    ],
    [onDelete],
  );

  const table = useReactTable({
    data: roles,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  const onRowClickHandler = (itemId: number | string) => {
    navigate(`/services/iam/role-detail/${itemId}`);
  };

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
                No roles found
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
    </>
  );
};
