import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { IPATResponse } from "@blocks-idp/iam/models/user";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { Trash } from "lucide-react";
import { useMemo, useState } from "react";
import { GenerateTokenModal } from "./generate-pat-modal";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button/copy-to-clipboard-button";
import useIsMobile from "@/hooks/use-is-mobile";
import { cn } from "@/lib/utils";
import { format } from "date-fns";

type PATListProps = {
  isLoading: boolean;
  data: IPATResponse[];
  id: string;
};

const LoadingSkelton = () => (
  <div className="grid gap-2">
    {Array.from({ length: 10 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

export const UserPATList = ({ isLoading, data, id }: PATListProps) => {
  const isMobile = useIsMobile();

  const columns: ColumnDef<IPATResponse>[] = useMemo(
    () => [
      {
        accessorKey: "note",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">PAT Name</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="flex w-[100px] items-center sm:w-[140px] lg:w-[180px]">
            <span className="truncate" title={row.original.note || "—"}>
              {row.original.note || "—"}
            </span>
          </div>
        ),
      },
      {
        accessorKey: "code",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Token</span>
          </div>
        ),
        cell: ({ row }) => {
          return (
            <div className="group flex w-[120px] flex-col sm:w-[150px] lg:w-[200px]">
              <div className="relative rounded-[4px] px-2 py-1">
                <span
                  className={cn(
                    "block truncate lg:overflow-visible lg:text-clip lg:whitespace-normal",
                    isMobile && "transition-all group-hover:blur-sm",
                  )}
                  title={row.original.code}
                >
                  {isMobile ? (
                    row.original.code
                  ) : (
                    <CopyToClipboardButton textToCopy={row.original.code} isHoverable={true}>
                      {row.original.code}
                    </CopyToClipboardButton>
                  )}
                </span>
                {isMobile && (
                  <div className="absolute right-0 top-1/2 -translate-y-1/2 opacity-0 transition-opacity group-hover:opacity-100">
                    <CopyToClipboardButton textToCopy={row.original.code} isHoverable={false}>
                      Copy
                    </CopyToClipboardButton>
                  </div>
                )}
              </div>
            </div>
          );
        },
      },
      {
        accessorKey: "expiryDate",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Valid till</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="flex w-[180px] items-center">
            {format(new Date(row.original.expiryDate), "MMM d, yyyy 'at' HH:mm")}
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
            {new Date(row.original.expiryDate).getTime() - new Date().getTime() > 0 ? (
              <Badge variant="success">active</Badge>
            ) : (
              <Badge variant="error">expired</Badge>
            )}
          </div>
        ),
      },
      {
        accessorKey: "Actions",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis"></span>
          </div>
        ),
        cell: () => (
          <Button disabled variant={"ghost"} className="hover:bg-transparent">
            <Trash size={16} />
          </Button>
        ),
      },
    ],
    [isMobile],
  );

  const table = useReactTable({
    data,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });
  const [isModalOpen, setIsModalOpen] = useState(false);

  if (isLoading) return <LoadingSkelton />;

  return (
    <>
      <div className="mb-5 flex items-center justify-between text-lg font-bold text-high-emphasis">
        <h1>PATs (Personal Access Tokens)</h1>
        <Button onClick={() => setIsModalOpen(true)} size="sm">
          Generate PAT
        </Button>
      </div>

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
                className="font-normal text-medium-emphasis"
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

      <GenerateTokenModal isOpen={isModalOpen} onClose={() => setIsModalOpen(false)} id={id} />
    </>
  );
};
