import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { IHistories } from "@blocks-idp/iam/models/user";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { formatDistanceToNow } from "date-fns";
// import { Check } from "lucide-react";
import { useMemo } from "react";

type HistoryListProps = {
  isLoading: boolean;
  data: IHistories[];
};

const LoadingSkelton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 10 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

const EVENT_TYPE = {
  issued_refresh_token: "Refresh Token Issued",
  revoke_access_by_logout: "Access Revoked (Logout)",
};

export const UserHistoryList = ({ isLoading, data }: HistoryListProps) => {
  const columns: ColumnDef<IHistories>[] = useMemo(
    () => [
      {
        accessorKey: "Event",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Event</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="flex w-[200px] items-center">
            {/* <Check className="mr-2 h-5 w-5 text-success" /> */}
            <span>{EVENT_TYPE[row.getValue("Event") as keyof typeof EVENT_TYPE]}</span>
          </div>
        ),
      },
      {
        accessorKey: "LastUpdatedDate",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Time</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="flex w-[200px] items-center">
            <span>
              {formatDistanceToNow(new Date(row.original.LastUpdatedDate), {
                addSuffix: true,
              })}
            </span>
          </div>
        ),
      },
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
            <TableCell colSpan={columns.length} className="h-24 text-center text-muted-foreground">
              No results.
            </TableCell>
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
};
