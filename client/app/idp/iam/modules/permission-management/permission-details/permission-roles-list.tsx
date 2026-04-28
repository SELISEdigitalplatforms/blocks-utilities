import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui-kits/table/table";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { IRole } from "@blocks-idp/iam/models/role";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { parseAsInteger, useQueryStates } from "nuqs";
import { Pagination } from "@/components/ui-kits/pagination/pagination";

type RolesTableProps = {
  slugs: string[];
};

const LoadingSkeleton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 5 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

export const PermissionRolesList = ({ slugs }: RolesTableProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const navigate = useNavigate();
  const [queryParams, setQueryParams] = useQueryStates({
    page: parseAsInteger.withDefault(0),
    pageSize: parseAsInteger.withDefault(10),
  });

  const { data, isLoading } = useGetRoles({
    projectKey: tenantId,
    page: queryParams.page,
    pageSize: queryParams.pageSize,
    filter: {
      search: "",
      slugs,
    },
  });

  const columns = useMemo<ColumnDef<IRole>[]>(
    () => [
      {
        id: "name",
        accessorFn: (row) => `${row.name}`.trim(),
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Name</span>
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
        cell: (roles) => (
          <div className="w-[150px] truncate">
            <span className="rounded-sm bg-blocks-primary-shades-300 px-2 py-1">{roles.row.original.slug}</span>
          </div>
        ),
      },
      {
        id: "description",
        accessorFn: (row) => `${row.description}`.trim(),
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Description</span>
          </div>
        ),
        cell: (roles) => <div className="w-[200px] truncate md:w-[260px]">{roles.row.original.description}</div>,
      },
    ],
    []
  );

  const table = useReactTable({
    data: data?.data || [],
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  const onRowClickHandler = (itemId: number | string) => {
    navigate(`/services/iam/role-detail/${itemId}`);
  };
  const onPageChangeHandler = (page: number) => {
    setQueryParams((params) => ({ ...params, page }));
  };

  const onPageSizeChangeHandler = (pageSize: number) => {
    setQueryParams((prev) => ({
      ...prev,
      pageSize,
      page: 0,
    }));
  };

  if (!slugs.length) return null;

  return (
    <Card className="mt-4">
      <CardHeader>
        <CardTitle>Assigned Roles</CardTitle>
      </CardHeader>
      <CardContent>
        {isLoading && <LoadingSkeleton />}
        {!isLoading && data && (
          <Table>
            <TableHeader>
              <TableRow className="px-4 py-3 hover:bg-transparent">
                {table
                  .getHeaderGroups()
                  .map((headerGroup) =>
                    headerGroup.headers.map((header) => (
                      <TableHead key={header.id}>
                        {header.isPlaceholder ? null : flexRender(header.column.columnDef.header, header.getContext())}
                      </TableHead>
                    ))
                  )}
              </TableRow>
            </TableHeader>
            <TableBody>
              {!data?.data.length ? (
                <TableRow>
                  <TableCell colSpan={columns.length} className="h-24 text-center text-muted-foreground">
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
                      <TableCell key={cell.id}>{flexRender(cell.column.columnDef.cell, cell.getContext())}</TableCell>
                    ))}
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        )}
        {!isLoading && data && (
          <div className="mt-5 flex items-center md:justify-end">
            <Pagination
              page={queryParams.page}
              onChange={onPageChangeHandler}
              totalCount={data.totalCount}
              pageSizeOptions={[5, 10, 20]}
              onPageSizeChange={onPageSizeChangeHandler}
              pageSize={queryParams.pageSize}
            />
          </div>
        )}
      </CardContent>
    </Card>
  );
};
