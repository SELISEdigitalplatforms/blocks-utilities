import { FilterControls, FilterToolbar, useSortQueryParams } from "@/components/filter-toolbar";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { ScrollArea, ScrollBar } from "@/components/ui-kits/scroll-area/scroll-area";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import useIsMobile from "@/hooks/use-is-mobile";
import { formatDate, parseDateString } from "@/lib/utils";
import { serviceRegistryService } from "@blocks-identifier/services/service-registery.service";
import { CLOUD_BUILTIN_SERVICES, TRACE_PROVIDERS } from "@blocks-lmt/constants/trace.constant";
import { useGetTraces } from "@blocks-lmt/hooks/use-trace";
import { TraceTree, getTypeColor } from "@blocks-lmt/models/trace.model";
import { TraceProviderSetupGuideLine } from "@blocks-lmt/components/trace-guideline/trace-provider-guideline";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { useQuery } from "@tanstack/react-query";
import { Archive, BookOpenText, Flame, Snowflake } from "lucide-react";
import { parseAsArrayOf, parseAsInteger, parseAsString, useQueryStates } from "nuqs";
import { useMemo, useState } from "react";

type TracesOverviewProps = {
  projectKey: string;
};

type TraceFilter = { search: string; services: string[] };

const useTracesFilterQueryParams = () => {
  const [queryParams, setQueryParams] = useQueryStates({
    search: parseAsString.withDefault(""),
    services: parseAsArrayOf(parseAsString).withDefault([]),
    page: parseAsInteger.withDefault(0),
    pageSize: parseAsInteger.withDefault(10),
  });

  return { queryParams, setQueryParams };
};

const useTraceSortQueryParams = () =>
  useSortQueryParams({ initial: { property: "Timestamp", isDescending: true } });

const TRACE_MODE_OPTIONS = [
  {
    value: "hot",
    title: "Hot",
    description: "Live and recent traces for active debugging.",
    Icon: Flame,
  },
  {
    value: "cold",
    title: "Cold",
    description: "Longer-term stored traces for later investigation.",
    Icon: Snowflake,
  },
  {
    value: "archive",
    title: "Archive",
    description: "Deep history retained for audit and export use cases.",
    Icon: Archive,
  },
] as const;

const LoadingSkelton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 10 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

function TracesList({
  data,
  isLoading,
  services,
}: {
  data: TraceTree[];
  isLoading: boolean;
  services: { label: string; value: string }[];
}) {
  const { sortQueryParams, setSortQueryParams } = useTraceSortQueryParams();

  const columns = useMemo<ColumnDef<TraceTree>[]>(
    () => [
      {
        accessorKey: "entryPoint",
        header: () => (
          <FilterControls.SortHeader
            id="OperationName"
            label="Entry point"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: ({ row }) => {
          const entryPoint = row.original.entryPoint;
          return (
            <div className="ml-2 flex w-[220px] flex-row items-center gap-2 sm:ml-0 sm:w-[320px]">
              <span className={`font-semibold uppercase ${getTypeColor(entryPoint.method)}`}>
                {entryPoint.method}
              </span>
              <span>{entryPoint.actionName}</span>
            </div>
          );
        },
      },
      {
        accessorKey: "service",
        header: () => (
          <FilterControls.SortHeader
            id="ServiceName"
            label="Service"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: ({ row }) => {
          const service = services.find((item) => item.value === row.original.serviceName);
          return <div className="ml-2 flex items-center sm:ml-0 sm:w-[180px]">{service?.label || row.original.serviceName}</div>;
        },
      },
      {
        accessorKey: "duration",
        header: () => (
          <FilterControls.SortHeader
            id="Duration"
            label="Duration"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: ({ row }) => <div className="ml-2 flex w-[180px] items-center sm:ml-0 sm:w-[150px]">{row.original.duration}ms</div>,
      },
      {
        accessorKey: "timestamp",
        header: () => (
          <FilterControls.SortHeader
            id="Timestamp"
            label="Timestamp"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: ({ row }) => {
          const dateValue = parseDateString(row.original.timestamp);
          return <div className="ml-2 w-[180px] lowercase sm:ml-0">{formatDate(dateValue)}</div>;
        },
      },
    ],
    [services, setSortQueryParams, sortQueryParams],
  );

  const table = useReactTable({
    data,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  if (isLoading) return <LoadingSkelton />;

  return (
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
              <TableRow key={row.id} className="text-medium-emphasis">
                {row.getVisibleCells().map((cell) => (
                  <TableCell key={cell.id}>
                    {flexRender(cell.column.columnDef.cell, cell.getContext())}
                  </TableCell>
                ))}
              </TableRow>
            ))
          ) : (
            <TableRow>
              <TableCell colSpan={table.getAllColumns().length} className="h-24 text-center text-muted-foreground">
                No results.
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
      <ScrollBar orientation="horizontal" />
    </ScrollArea>
  );
}

export function TracesOverview({ projectKey }: TracesOverviewProps) {
  const isMobile = useIsMobile();
  const { queryParams, setQueryParams } = useTracesFilterQueryParams();
  const { sortQueryParams } = useTraceSortQueryParams();
  const [tabId, setTabId] = useState("hot");
  const [open, setOpen] = useState(false);
  const [provider, setProvider] = useState<TRACE_PROVIDERS>(TRACE_PROVIDERS.hot);

  const { data: registeredServices } = useQuery({
    queryKey: ["registered-services", projectKey],
    queryFn: () =>
      serviceRegistryService.getAllServices({
        page: 0,
        pageSize: 1000,
        projectKey,
        filter: { serviceId: "", serviceName: "", serviceType: "" },
      }),
    enabled: !!projectKey,
  });

  const { data, isLoading, isFetching, refetch } = useGetTraces({
    page: queryParams.page,
    pageSize: queryParams.pageSize,
    projectKey,
    search: queryParams.search,
    sort: sortQueryParams,
    filter: {
      services: queryParams.services,
      excepts: ["blocks-lmt-api"],
    },
  });

  const loading = isLoading || isFetching;

  const allServices = useMemo(() => {
    const registered = registeredServices?.data || [];
    const merged = [
      ...CLOUD_BUILTIN_SERVICES,
      ...registered.map((service) => ({ label: service.name, value: service.serviceId })),
    ];
    return merged.filter((item, index, array) => array.findIndex((value) => value.value === item.value) === index);
  }, [registeredServices?.data]);

  const pageChangeHandler = (page: number) => {
    setQueryParams((params) => ({ ...params, page }));
  };

  const pageSizeChangeHandler = (pageSize: number) => {
    setQueryParams((params) => ({ ...params, page: 0, pageSize }));
  };

  const tabChangedHandler = (value: keyof typeof TRACE_PROVIDERS) => {
    pageChangeHandler(0);
    setTabId(value);
    setProvider(TRACE_PROVIDERS[value]);
  };

  const changeHandler = (key: string, value: unknown) => {
    setQueryParams((params) => ({
      ...params,
      [key]: Array.isArray(value) ? [...value] : value,
      page: 0,
    }));
  };

  const resetHandler = () => setQueryParams(null);

  return (
    <main>
      <Tabs value={tabId} onValueChange={(value: string) => tabChangedHandler(value as keyof typeof TRACE_PROVIDERS)}>
        <div className="mb-5 flex flex-col gap-4 rounded-xl border bg-gradient-to-br from-slate-50 to-white p-4">
          <div className="flex items-center justify-between gap-3">
            <div>
              <p className="text-sm font-medium text-high-emphasis">Trace storage modes</p>
              <p className="text-sm text-muted-foreground">Choose the trace tier you want to inspect.</p>
            </div>
            <Button onClick={() => setOpen((current) => !current)} variant="outline" size="sm">
              <BookOpenText className="aspect-square w-4" />
              <span className="sr-only sm:not-sr-only sm:ml-2">Guide</span>
            </Button>
          </div>

          {isMobile ? (
            <Select value={tabId} onValueChange={(value: string) => tabChangedHandler(value as keyof typeof TRACE_PROVIDERS)}>
              <SelectTrigger className="w-full sm:w-48">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {TRACE_MODE_OPTIONS.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.title}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          ) : (
            <div className="grid grid-cols-3 gap-3">
              {TRACE_MODE_OPTIONS.map((option) => {
                const Icon = option.Icon;
                const isActive = tabId === option.value;

                return (
                  <button
                    key={option.value}
                    type="button"
                    onClick={() => tabChangedHandler(option.value as keyof typeof TRACE_PROVIDERS)}
                    className={[
                      "rounded-xl border p-4 text-left transition-all",
                      isActive
                        ? "border-primary bg-primary/5 shadow-sm ring-1 ring-primary/20"
                        : "border-border bg-background hover:border-primary/40 hover:bg-accent/30",
                    ].join(" ")}
                  >
                    <div className="flex items-center gap-3">
                      <div className={[
                        "rounded-lg p-2",
                        isActive ? "bg-primary/10 text-primary" : "bg-muted text-muted-foreground",
                      ].join(" ")}>
                        <Icon className="h-4 w-4" />
                      </div>
                      <div>
                        <div className="font-medium text-high-emphasis">{option.title}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{option.description}</div>
                      </div>
                    </div>
                  </button>
                );
              })}
            </div>
          )}
        </div>

        <TabsContent value="hot">
          <Card>
            <CardHeader>
              <FilterToolbar<TraceFilter>
                filters={[
                  { key: "search", type: "SearchInput", label: "" },
                  {
                    key: "services",
                    type: "MultiSelect",
                    label: "Service",
                    props: { options: allServices },
                  },
                ]}
                values={{ search: queryParams.search, services: queryParams.services }}
                defaultValues={{ search: "", services: [] }}
                onChange={(key, value) => changeHandler(String(key), value)}
                onReset={resetHandler}
              />
            </CardHeader>
            <CardContent>
              <TracesList data={data?.data || []} isLoading={loading} services={allServices} />
              {!loading && data && data.totalCount > queryParams.pageSize && (
                <div className="mt-5 flex items-center md:justify-end">
                  <Pagination
                    page={queryParams.page}
                    pageSize={queryParams.pageSize}
                    pageSizeOptions={[10, 20, 50]}
                    onChange={pageChangeHandler}
                    onPageSizeChange={pageSizeChangeHandler}
                    totalCount={data.totalCount || 0}
                  />
                </div>
              )}
            </CardContent>
          </Card>
        </TabsContent>
        <TabsContent value="cold">
          <Card>
            <CardContent className="flex h-[500px] items-center justify-center text-muted-foreground">
              Coming soon
            </CardContent>
          </Card>
        </TabsContent>
        <TabsContent value="archive">
          <Card>
            <CardContent className="flex h-[500px] items-center justify-center text-muted-foreground">
              Coming soon
            </CardContent>
          </Card>
        </TabsContent>
        {!isMobile ? (
          <TraceProviderSetupGuideLine open={open} onOpenChange={setOpen} provider={provider} />
        ) : null}
      </Tabs>
    </main>
  );
}