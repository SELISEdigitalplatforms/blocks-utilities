
import { Pencil } from "lucide-react";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui-kits/table/table";
import { Button } from "@/components/ui-kits/button/button";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Badge } from "@/components/ui-kits/badge/badge";
import { useMemo } from "react";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import {
  IPermission,
  PERMISSION_SEVERITY_OPTIONS,
  PermissionSeverityLevel,
  ResourceType,
} from "@blocks-idp/iam/models/permission";
import { FilterControls } from "@/components/filter-toolbar";
import { usePermissionsSortQuaryParams } from "./permissions-filter-toolbar";
import { Link } from "react-router-dom";
import { useNavigate } from "react-router-dom";
import { cn } from "@/lib/utils";
import { ScrollArea, ScrollBar } from "@/components/ui-kits/scroll-area/scroll-area";

type PermissionTableProps = {
  permissions: IPermission[];
  isLoading: boolean;
};

const LoadingSkelton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 5 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

export const PermissionSeverityBadge = ({ severity }: { severity: PermissionSeverityLevel }) => {
  const config = PERMISSION_SEVERITY_OPTIONS.find((option) => option.value === severity);
  if (!config) return null;
  return (
    <Badge variant={config.variant} className={cn(config.className, config.bg)}>
      {config.label}
    </Badge>
  );
};

export const PermissionsList = ({ permissions, isLoading }: PermissionTableProps) => {
  const { sortQueryParams, setSortQueryParams } = usePermissionsSortQuaryParams();
  const navigate = useNavigate();

  const columns = useMemo<ColumnDef<IPermission>[]>(
    () => [
      {
        id: "name",
        accessorFn: (row) => `${row.name}`.trim(),
        header: () => (
          <FilterControls.SortHeader label="Name" id="Name" value={sortQueryParams} onChange={setSortQueryParams} />
        ),
        cell: (permission) => (
          <div className="flex w-[200px] items-center break-all" title={permission.row.original.name}>
            <span>{permission.row.original.name}</span>
          </div>
        ),
      },
      {
        id: "resource",
        accessorFn: (row) => `${row.resource}`.trim(),
        header: () => (
          <FilterControls.SortHeader
            label="Resource"
            id="Resource"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),

        cell: (permission) => (
          <div className="flex w-[180px] items-center break-all">
            <span>{permission.row.original.resource}</span>
          </div>
        ),
      },
      {
        id: "isBuiltIn",
        accessorFn: (row) => `${row.isBuiltIn}`.trim(),
        header: () => {
          return (
            <div className="flex items-center">
              <span className="font-bold text-medium-emphasis">Source</span>
            </div>
          );
        },

        cell: (permission) => (
          <div className="flex w-[180px] items-center break-all">
            <Badge
              className={cn(
                permission.row.original.isBuiltIn ? "!bg-gray-300 !text-gray-800" : "!bg-purple-100 !text-purple-700"
              )}
            >
              {permission.row.original.isBuiltIn ? "Built In" : "Custom"}
            </Badge>
          </div>
        ),
      },
      {
        id: "Type",
        accessorFn: (row) => `${row.type}`.trim(),
        header: () => (
          <FilterControls.SortHeader label="Type" id="Type" value={sortQueryParams} onChange={setSortQueryParams} />
        ),

        cell: (permission) => {
          const resourceTypeKey = permission.row.original.type as number;
          const resourceName = ResourceType[resourceTypeKey] as string;

          return (
            <div className="flex w-[150px] items-center">
              <span>{resourceName}</span>
            </div>
          );
        },
      },
      {
        id: "permissionSeverity",
        accessorFn: (row) => `${row.permissionSeverity}`.trim(),
        header: () => {
          return (
            <div className="flex items-center">
              <span className="font-bold text-medium-emphasis">Severity</span>
            </div>
          );
        },
        cell: (permission) => {
          return (
            <div className="flex w-[70px] items-center justify-center">
              <PermissionSeverityBadge severity={permission.row.original.permissionSeverity} />
            </div>
          );
        },
      },

      {
        id: "rolesCount",
        accessorFn: (row) => `${row.roles.length}`.trim(),
        header: () => {
          return (
            <div className="flex items-center">
              <span className="font-bold text-medium-emphasis">No of Roles</span>
            </div>
          );
        },
        cell: (permission) => {
          return (
            <div className="flex w-[70px] items-center justify-end">
              <span>{permission.row.original.roles.length}</span>
            </div>
          );
        },
      },

      {
        id: "tags",
        accessorFn: (row) => `${row.description}`.trim(),
        header: () => {
          return <div>Tags</div>;
        },
        cell: (tags) => (
          <div className="flex max-w-[150px] flex-wrap gap-1">
            {tags.row.original.tags.length > 0 && <Badge variant="secondary">{tags.row.original.tags[0]}</Badge>}

            {tags.row.original.tags.length - 1 > 0 && (
              <TooltipProvider>
                <Tooltip>
                  <TooltipTrigger>
                    <Badge>{tags.row.original.tags.length - 1}+</Badge>
                  </TooltipTrigger>
                  <TooltipContent className="flex max-w-[200px] flex-wrap gap-2 p-1">
                    {tags.row.original.tags.slice(1).map((item, index) => (
                      <Badge key={index} variant="secondary">
                        {item}
                      </Badge>
                    ))}
                  </TooltipContent>
                </Tooltip>
              </TooltipProvider>
            )}
          </div>
        ),
      },
      {
        id: "description",
        accessorFn: (row) => `${row.description}`.trim(),
        header: () => {
          return (
            <div className="flex items-center">
              <span className="font-bold text-medium-emphasis">Description</span>
            </div>
          );
        },
        cell: (permission) => (
          <div className="flex w-[200px]">
            <span className="truncate text-sm lowercase text-medium-emphasis">
              {permission.row.original.description}
            </span>
          </div>
        ),
      },
      {
        id: "actions",
        enableHiding: false,
        cell: ({ row }) => (
          <div className="flex">
            {!row.original.isBuiltIn && (
              <Link to={`/services/iam/permission-detail/${row.original.itemId}`}>
                <Button size="icon" className="rounded-full" variant="ghost">
                  <Pencil className="h-4 w-4" />
                </Button>
              </Link>
            )}
          </div>
        ),
      },
    ],
    [setSortQueryParams, sortQueryParams]
  );

  const table = useReactTable({
    data: permissions,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  if (isLoading) return <LoadingSkelton />;

  return (
    <ScrollArea className="w-full">
      <Table className="text-sm ">
        <TableHeader>
          {table.getHeaderGroups().map((headerGroup) => (
            <TableRow key={headerGroup.id} className="px-4 py-3 hover:bg-transparent">
              {headerGroup.headers.map((header) => (
                <TableHead key={header.id} className="font-bold text-medium-emphasis">
                  {header.isPlaceholder ? null : flexRender(header.column.columnDef.header, header.getContext())}
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
                onClick={() => {
                  navigate(`/services/iam/permission-detail/${row.original.itemId}`);
                }}
                isHoverable
              >
                {row.getVisibleCells().map((cell) => (
                  <TableCell key={cell.id}>{flexRender(cell.column.columnDef.cell, cell.getContext())}</TableCell>
                ))}
              </TableRow>
            ))
          ) : (
            <TableRow>
              <TableCell colSpan={columns.length} className="h-24 text-center text-muted-foreground">
                No permission found. Please create new permission.
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
      <ScrollBar orientation="horizontal" />
    </ScrollArea>
  );
};
