import React, { useMemo } from "react";
import { ScrollArea, ScrollBar } from "@/components/ui-kits/scroll-area/scroll-area";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { formatDate, parseDateString } from "@/lib/utils";
import { MagicUrl } from "@blocks-utilities/models/magic-url.model";
import { useMagicUrlSortQueryParams } from "./magic-urls-filter-toolbar";
import { FilterControls } from "@/components/filter-toolbar";
import { Button } from "@/components/ui-kits/button/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { EllipsisVertical, CircleSlash, Eye, ExternalLink } from "lucide-react";
import { MagicUrlStatusBadge } from "./magic-url-status-badge";
import { useNavigate } from "react-router-dom";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { useProjectStore } from "@/store/useProjectStore";
import { useDeactivateMagicUrl } from "@blocks-utilities/hooks/use-deactivate-magic-url";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";

const LoadingSkelton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 10 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

type MagicUrlsListProps = {
  data: MagicUrl[];
  isLoading: boolean;
};

export function MagicUrlsList({ data, isLoading }: MagicUrlsListProps) {
  const { sortQueryParams, setSortQueryParams } = useMagicUrlSortQueryParams();
  const navigate = useNavigate();
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  const { deactivateMagicUrl, isRemoving } = useDeactivateMagicUrl();
  const [itemToDeactivate, setItemToDeactivate] = React.useState<string | null>(null);
  const [isDeactivateModalOpen, setIsDeactivateModalOpen] = React.useState(false);

  const handleDeactivate = (id: string) => {
    setItemToDeactivate(id);
    setIsDeactivateModalOpen(true);
  };

  const confirmDeactivate = () => {
    if (itemToDeactivate) {
      deactivateMagicUrl(itemToDeactivate, tenantId, () => {
        setIsDeactivateModalOpen(false);
        setItemToDeactivate(null);
      });
    }
  };

  const handleViewDetails = (itemId: string) => {
    navigate(`/utilities/magic-url/details/${itemId}`);
  };

  const columns = useMemo<ColumnDef<MagicUrl>[]>(
    () => [
      {
        accessorKey: "uri",
        header: () => (
          <FilterControls.SortHeader
            id="Uri"
            label="URL"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: ({ row }) => (
          <div className="ml-2 flex flex-col sm:ml-0">
            <CopyToClipboardButton textToCopy={row.original.shortUri} isHoverable>
              <span className="truncate font-medium">{row.original.shortUri}</span>
            </CopyToClipboardButton>
            <span
              className="w-[420px] truncate text-xs text-muted-foreground"
              title={row.original.uri}
            >
              {row.original.uri}
            </span>
          </div>
        ),
      },
      {
        accessorKey: "name",
        header: () => (
          <FilterControls.SortHeader
            id="Name"
            label="Name"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: ({ row }) => (
          <div className="ml-2 flex flex-col sm:ml-0">
            <span className="truncate font-medium" title={row.original.name}>
              {row.original.name || "-"}
            </span>
          </div>
        ),
      },
      {
        accessorKey: "usageLimit",
        header: "Usage Limit",
        cell: ({ row }) => {
          const limit = row.getValue("usageLimit") as number;
          return (
            <div className="ml-2 flex w-[100px] items-center sm:ml-0">
              <span>{limit === 0 ? "Unlimited" : limit}</span>
            </div>
          );
        },
      },
      {
        accessorKey: "expiryDate",
        header: "Scheduled Expiry Date",
        cell: ({ row }) => {
          const dateStr = row.getValue("expiryDate") as string | undefined;
          if (!dateStr) return <div className="ml-2 w-[180px] sm:ml-0">-</div>;
          const dateValue = parseDateString(dateStr);
          const formattedDate = formatDate(dateValue);
          return <div className="ml-2 w-[180px] lowercase sm:ml-0">{formattedDate}</div>;
        },
      },
      {
        accessorKey: "status",
        header: "Status",
        cell: ({ row }) => (
          <div className="ml-2 flex w-[120px] items-center sm:ml-0">
            <MagicUrlStatusBadge item={row.original} />
          </div>
        ),
      },
      {
        accessorKey: "requestMethod",
        header: "Request Method",
        cell: ({ row }) => (
          <div className="ml-2 flex w-[120px] items-center sm:ml-0">
            <span className="uppercase">{row.getValue("requestMethod") || "-"}</span>
          </div>
        ),
      },
      {
        accessorKey: "clientCredential",
        header: "Client Credential",
        cell: ({ row }) => (
          <div className="max-w-[150px] truncate" title={row.getValue("clientCredential")}>
            {row.getValue("clientCredential") || "-"}
          </div>
        ),
      },
      {
        id: "actions",
        enableHiding: false,
        cell: ({ row }) => (
          <div onClick={(e) => e.stopPropagation()}>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" className="h-5 w-5 p-0" disabled={isRemoving}>
                  <EllipsisVertical width={20} height={20} />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem
                  className="cursor-pointer"
                  onClick={() => handleViewDetails(row.original.itemId)}
                >
                  <Eye className="mr-2 h-4 w-4" />
                  <span>View Details</span>
                </DropdownMenuItem>
                <DropdownMenuItem
                  className="cursor-pointer"
                  onClick={(e) => {
                    e.stopPropagation();
                    window.open(row.original.shortUri, "_blank");
                  }}
                >
                  <ExternalLink className="mr-2 h-4 w-4" />
                  <span>Go to Link</span>
                </DropdownMenuItem>
                <DropdownMenuItem
                  className="cursor-pointer text-error"
                  onClick={(e) => {
                    e.stopPropagation();
                    handleDeactivate(row.original.itemId);
                  }}
                  disabled={isRemoving}
                >
                  <CircleSlash className="mr-2 h-4 w-4" />
                  <span>{isRemoving ? "Removing..." : "Deactivate"}</span>
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        ),
      },
    ],
    [setSortQueryParams, sortQueryParams, isRemoving],
  );

  const table = useReactTable({
    data,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  if (isLoading) return <LoadingSkelton />;

  return (
    <>
      <ScrollArea className="w-full">
        <Table className="text-sm">
          <TableHeader>
            {table.getHeaderGroups().map((headerGroup) => (
              <TableRow key={headerGroup.id} className="px-4 py-2 hover:bg-transparent">
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
                  className="cursor-pointer text-medium-emphasis"
                  isHoverable
                  onClick={() => handleViewDetails(row.original.itemId)}
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
                  colSpan={table.getAllColumns().length}
                  className="h-24 text-center text-muted-foreground"
                >
                  No results.
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
        <ScrollBar orientation="horizontal" />
      </ScrollArea>
      <Dialog open={isDeactivateModalOpen} onOpenChange={setIsDeactivateModalOpen}>
        <ConfirmationModal
          onCancel={() => setIsDeactivateModalOpen(false)}
          onConfirm={confirmDeactivate}
          data={{
            dialogTitle: "Deactivate Magic URL",
            dialogSubtitle: "Are you sure you want to deactivate this Magic URL? This action cannot be undone.",
            confirmButton: "Deactivate",
            cancelButton: "Cancel",
          }}
          buttonState={{ confirm: { disable: isRemoving } }}
        />
      </Dialog>
    </>
  );
}
