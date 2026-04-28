import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { X } from "lucide-react";

import { IRole } from "@blocks-idp/iam/models/role";

type UserRolesListProps = {
  roles: IRole[];
  isLoading: boolean;
  userId: string;
  projectKey: string;
  onRemoveRole: (slug: string) => void;
};

const LoadingSkelton = () => (
  <div className="grid w-full grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
    {Array.from({ length: 5 }).map((_, index) => (
      <div key={index} className="col-span-1 flex items-center py-2">
        <div className="flex w-full items-center rounded-2xl border border-border px-4 py-2 shadow-sm">
          <div className="flex min-w-0 flex-1 flex-col space-y-1">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-3 w-16" />
          </div>
          <div className="ml-2 flex items-center">
            <Skeleton className="h-7 w-7 rounded-md" />
          </div>
        </div>
      </div>
    ))}
  </div>
);

export const UserRolesList = ({
  roles,
  isLoading,
  userId,
  projectKey,
  onRemoveRole,
}: UserRolesListProps) => {
  if (isLoading) return <LoadingSkelton />;

  return (
    <>
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
        {isLoading
          ? // Show skeletons while loading
            Array.from({ length: 5 }).map((_, idx) => (
              <div key={idx} className="col-span-1 flex items-center py-2">
                <div className="h-4 w-4 rounded bg-gray-200" />
                <div className="h-4 w-24 rounded bg-gray-200" />
                <div className="h-4 w-20 rounded bg-gray-200" />
              </div>
            ))
          : roles &&
            roles.length > 0 &&
            roles.map((item) => (
              <div key={item.itemId} className="col-span-1 flex items-center py-2">
                <div className="flex w-full items-center rounded-2xl border border-border px-4 py-2 shadow-sm">
                  <div className="flex min-w-0 flex-1 flex-col">
                    <span
                      className="truncate text-base font-medium leading-tight"
                      title={item.name}
                    >
                      {item.name}
                    </span>
                    <span
                      className="truncate text-xs lowercase text-muted-foreground"
                      title={item.slug}
                    >
                      {item.slug}
                    </span>
                  </div>
                  <div className="ml-2 flex items-center">
                    <button
                      type="button"
                      className="flex h-7 w-7 items-center justify-center rounded-md bg-secondary transition-colors hover:bg-secondary/80"
                      onClick={() => onRemoveRole(item.slug)}
                      aria-label="Remove role"
                    >
                      <X className="h-4 w-4" />
                    </button>
                  </div>
                </div>
              </div>
            ))}
      </div>
      {!isLoading && roles && roles.length === 0 && (
        <div className="flex h-24 items-center justify-center text-muted-foreground">
          No roles found
        </div>
      )}
      {/* <Table>
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
      </Table> */}
    </>
  );
};
