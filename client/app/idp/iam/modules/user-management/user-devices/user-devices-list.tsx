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
import { IDeviceSession } from "@blocks-idp/iam/models/user";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { formatDistanceToNow } from "date-fns";
import { useMemo } from "react";

type DeviceListProps = {
  isLoading: boolean;
  data: IDeviceSession[];
};

const LoadingSkelton = () => (
  <div className="grid gap-2">
    {Array.from({ length: 10 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

export const UserDevicesList = ({ isLoading, data }: DeviceListProps) => {
  const columns: ColumnDef<IDeviceSession>[] = useMemo(
    () => [
      {
        accessorKey: "accessFrom",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Accessed from</span>
          </div>
        ),
        cell: ({ row }) => {
          const deviceInfo = row.original.DeviceInformation;
          return (
            <div className="flex w-[200px] flex-col">
              <div className="mb-2 flex items-center">
                <span>IP:</span>
                <div className="ml-2 rounded-[4px] bg-blocks-primary-shades-300 px-2 py-1">
                  <span>{row.original.IpAddresses}</span>
                </div>
              </div>
              <div className="flex flex-col">
                <span>
                  {deviceInfo.Device
                    ? `${deviceInfo.Device.charAt(0).toUpperCase() + deviceInfo.Device.slice(1)}`
                    : "Unknown Device"}{" "}
                  {deviceInfo.Model
                    ? `${deviceInfo.Model.charAt(0).toUpperCase() + deviceInfo.Model.slice(1)}`
                    : ""}
                </span>
                <span>
                  {deviceInfo.Browser ? `${deviceInfo.Browser}` : "Unknown Browser"}{" "}
                  {deviceInfo.OS ? `on ${deviceInfo.OS}` : " "}
                </span>
              </div>
            </div>
          );
        },
      },
      {
        accessorKey: "IssuedUtc",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Last Accessed</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="flex w-[180px] items-center">
            {formatDistanceToNow(new Date(row.original.IssuedUtc), { addSuffix: true })}
          </div>
        ),
      },
      {
        accessorKey: "status",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Status</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="flex w-[180px] items-center">
            {new Date(row.original.ExpiresUtc).getTime() - new Date().getTime() > 0 ? (
              <Badge variant="success">active</Badge>
            ) : (
              <Badge variant="error">expired</Badge>
            )}
          </div>
        ),
      },
    ],
    [],
  );

  const table = useReactTable({
    data,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  if (isLoading) return <LoadingSkelton />;

  return (
    <>
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
          {table.getRowModel().rows?.length ? (
            table.getRowModel().rows.map((row) => (
              <TableRow
                key={row.id}
                data-state={row.getIsSelected() && "selected"}
                className="cursor-pointer font-normal text-medium-emphasis"
              >
                {row.getVisibleCells().map((cell) => (
                  <TableCell key={cell.id}>
                    {flexRender(cell.column.columnDef.cell, cell.getContext())}
                  </TableCell>
                ))}
              </TableRow>
            ))
          ) : (
            <TableRow>
              <TableCell
                colSpan={columns.length}
                className="h-24 text-center text-muted-foreground"
              >
                No results.
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
    </>
  );
};
