

import React, { useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  ColumnDef,
  ColumnFiltersState,
  SortingState,
  VisibilityState,
  flexRender,
  getCoreRowModel,
  getFacetedRowModel,
  getFacetedUniqueValues,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable,
} from "@tanstack/react-table";
import { ArrowUpDown, CirclePlus, FileClock, Settings } from "lucide-react";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { Button } from "@/components/ui-kits/button/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { IMessagingServiceData } from "@blocks-communication/mail/models/messaging";
import { Dialog, DialogTrigger } from "@/components/ui-kits/dialog/dialog";
import CampaignCreation from "@blocks-communication/mail/components/messaging/campaign-creation/campaign-creation";
import { ScrollArea, ScrollBar } from "@/components/ui-kits/scroll-area/scroll-area";
import { compareDates, formatDate, parseDateString } from "@/lib/utils";
import {
  messagingServiceData,
  configurations,
  protocols,
} from "@blocks-communication/mail/constants/messaging";
import { MessagingTableToolbar } from "@blocks-communication/mail/components/messaging/messaging-table-toolbar/messaging-table-toolbar";
import TablePagination from "@/components/ui-kits/table-pagination/table-pagination";

const data = messagingServiceData;

export function MessagingServiceTable() {
  const navigate = useNavigate();

  const [sorting, setSorting] = useState<SortingState>([{ id: "lastModified", desc: true }]);
  const [columnFilters, setColumnFilters] = React.useState<ColumnFiltersState>([]);
  const [columnVisibility, setColumnVisibility] = React.useState<VisibilityState>({});
  const [rowSelection, setRowSelection] = React.useState({});

  const handleRowClick = (messageId: number | string) => {
    navigate(`/utilities/messaging/campaigns/${messageId}`);
  };

  const columns: ColumnDef<IMessagingServiceData>[] = [
    {
      accessorKey: "name",
      header: ({ column }) => {
        return (
          <div className="flex items-center sm:w-[250px]">
            <span className="font-bold text-medium-emphasis">Name</span>
            <ArrowUpDown
              onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
              className="ml-2 h-4 w-4 cursor-pointer text-high-emphasis hover:text-low-emphasis"
            />
          </div>
        );
      },
      cell: ({ row }) => (
        <div className="ml-2 truncate lowercase sm:ml-0 sm:w-[250px]">{row.getValue("name")}</div>
      ),
    },
    {
      accessorKey: "configuration",
      header: ({ column }) => {
        return (
          <div className="flex items-center sm:w-[250px]">
            <span className="font-bold text-medium-emphasis">Configuration</span>
            <ArrowUpDown
              onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
              className="ml-2 h-4 w-4 cursor-pointer text-high-emphasis hover:text-low-emphasis"
            />
          </div>
        );
      },
      cell: ({ row }) => {
        const configuration = configurations.find(
          (configuration) => configuration.value === row.getValue("configuration"),
        );

        if (!configuration) {
          return null;
        }

        return (
          <div className="ml-2 flex items-center sm:ml-0 sm:w-[250px]">
            <span>{configuration.label}</span>
          </div>
        );
      },
      filterFn: (row, id, filterValue: { text?: string; types?: string[] }) => {
        return filterValue.types ? filterValue.types.includes(row.getValue(id)) : true;
      },
    },
    {
      accessorKey: "protocol",
      header: ({ column }) => {
        return (
          <div className="flex items-center sm:w-[100px]">
            <span className="font-bold text-medium-emphasis">Protocol</span>
            <ArrowUpDown
              onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
              className="ml-2 h-4 w-4 cursor-pointer text-high-emphasis hover:text-low-emphasis"
            />
          </div>
        );
      },
      cell: ({ row }) => {
        const protocol = protocols.find((protocol) => protocol.value === row.getValue("protocol"));

        if (!protocol) {
          return null;
        }

        return (
          <div className="ml-2 flex items-center sm:ml-0">
            <span>{protocol.label}</span>
          </div>
        );
      },
      filterFn: (row, id, filterValue: { text?: string; types?: string[] }) => {
        return filterValue.types ? filterValue.types.includes(row.getValue(id)) : true;
      },
    },
    {
      accessorKey: "lastModified",
      header: ({ column }) => (
        <div className="flex items-center sm:w-[150px]">
          <span className="font-bold text-medium-emphasis">Last Modified</span>
          <ArrowUpDown
            onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
            className="ml-2 h-4 w-4 cursor-pointer text-high-emphasis hover:text-low-emphasis"
          />
        </div>
      ),
      cell: ({ row }) => {
        const dateValue = parseDateString(row.getValue("lastModified"));
        const formattedDate = formatDate(dateValue);
        return <div className="ml-2 lowercase sm:ml-0">{formattedDate}</div>;
      },
      sortingFn: (rowA, rowB, columnId) => {
        return compareDates(rowA.getValue(columnId), rowB.getValue(columnId));
      },
    },
    {
      accessorKey: "createdOn",
      header: ({ column }) => (
        <div className="flex w-[150px] items-center">
          <span className="font-bold text-medium-emphasis">Created on</span>
          <ArrowUpDown
            onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
            className="ml-2 h-4 w-4 cursor-pointer text-high-emphasis hover:text-low-emphasis"
          />
        </div>
      ),
      cell: ({ row }) => {
        const dateValue = parseDateString(row.getValue("createdOn"));
        const formattedDate = formatDate(dateValue, true);
        return <div className="ml-2 px-6 lowercase sm:ml-0">{formattedDate}</div>;
      },
      sortingFn: (rowA, rowB, columnId) => {
        return compareDates(rowA.getValue(columnId), rowB.getValue(columnId));
      },
    },
    {
      accessorKey: "createdBy",
      header: ({ column }) => {
        return (
          <div className="flex w-[150px] items-center">
            <span className="font-bold text-medium-emphasis">Created by</span>
            <ArrowUpDown
              onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
              className="ml-2 h-4 w-4 cursor-pointer text-high-emphasis hover:text-low-emphasis"
            />
          </div>
        );
      },
      cell: ({ row }) => (
        <div className="ml-2 truncate lowercase sm:ml-0 sm:w-[150px]">
          {row.getValue("createdBy")}
        </div>
      ),
    },
  ];
  const table = useReactTable({
    data,
    columns,
    enableRowSelection: true,
    onRowSelectionChange: setRowSelection,
    onSortingChange: setSorting,
    onColumnFiltersChange: setColumnFilters,
    onColumnVisibilityChange: setColumnVisibility,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFacetedRowModel: getFacetedRowModel(),
    getFacetedUniqueValues: getFacetedUniqueValues(),
    state: {
      sorting,
      columnFilters,
      columnVisibility,
      rowSelection,
    },
    meta: {
      navigate,
      handleRowClick,
    },
  });

  return (
    <main className="flex flex-col">
      <div className="flex w-full flex-col">
        <div className="flex w-full justify-between text-high-emphasis">
          <h3 className="text-2xl font-bold tracking-tight">Messaging</h3>
          <div>
            <Button variant="outline" size="sm" className="ml-4 gap-1 text-sm font-medium">
              <FileClock className="h-5 w-5" />
              <span className="sr-only sm:not-sr-only">Logs</span>
            </Button>
            <Button
              variant="outline"
              size="sm"
              className="ml-4 gap-1 text-sm font-medium"
              onClick={() => navigate("/utilities/messaging/configure")}
            >
              <Settings className="h-5 w-5" />
              <span className="sr-only sm:not-sr-only">Configure</span>
            </Button>
          </div>
        </div>
        <Tabs defaultValue="users" className="mt-[18px] flex w-full flex-col md:mt-[24px]">
          <div className="mb-5 flex items-center text-base">
            <TabsList className="h-[42px] bg-blocks-primary-shades-300">
              <TabsTrigger value="users" className="h-8">
                Messages
              </TabsTrigger>
              <TabsTrigger value="sign-in-methods" className="h-8">
                Reports
              </TabsTrigger>
            </TabsList>
            <div className="ml-auto flex items-center gap-2">
              <Dialog>
                <DialogTrigger asChild>
                  <Button
                    size="default"
                    variant="default"
                    className="bg-primary text-primary-foreground shadow-none"
                  >
                    <CirclePlus className="h-5 w-5 lg:mr-2" />
                    <span className="sr-only lg:not-sr-only">Add</span>
                  </Button>
                </DialogTrigger>
                <CampaignCreation dialogTitle="Add Messages" />
              </Dialog>
            </div>
          </div>
          <TabsContent value="users">
            <Card className="rounded shadow-none">
              <CardHeader className="px-7">
                <CardTitle className="mb-3 text-xl text-high-emphasis">Messages</CardTitle>
                <CardDescription className="mb-5 text-base font-normal text-medium-emphasis">
                  Lorem ipsum dolor sit amet consectetur
                </CardDescription>
              </CardHeader>
              <div className="px-7">
                <MessagingTableToolbar table={table} />
              </div>
              <CardContent>
                <ScrollArea className="w-full">
                  <Table className="text-sm">
                    <TableHeader>
                      {table.getHeaderGroups().map((headerGroup) => (
                        <TableRow key={headerGroup.id} className="px-4 py-3 hover:bg-transparent">
                          {headerGroup.headers.map((header) => {
                            return (
                              <TableHead key={header.id} className="font-bold text-medium-emphasis">
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
                          <TableRow
                            key={row.id}
                            data-state={row.getIsSelected() && "selected"}
                            className="cursor-pointer font-normal text-medium-emphasis"
                            onClick={() => handleRowClick(row.original.id)}
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
                          <TableCell colSpan={columns.length} className="h-24 text-center">
                            No results.
                          </TableCell>
                        </TableRow>
                      )}
                    </TableBody>
                  </Table>
                  <ScrollBar orientation="horizontal" />
                </ScrollArea>
              </CardContent>
              <TablePagination table={table} />
            </Card>
          </TabsContent>
        </Tabs>
      </div>
    </main>
  );
}
