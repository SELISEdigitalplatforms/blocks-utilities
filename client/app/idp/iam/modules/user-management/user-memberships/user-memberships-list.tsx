
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { useMemo, useState } from "react";
import { IMembership } from "@blocks-idp/iam/models/user";
import { Badge } from "@/components/ui-kits/badge/badge";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Button } from "@/components/ui-kits/button/button";
import { EllipsisVertical, Settings, XCircle } from "lucide-react";
import { RemoveMembership } from "./remove-membership";
import { EditMembership } from "./edit-membership";

type UserMembershipsListProps = {
  memberships: IMembership[];
  orgNameMap: Map<string, string>;
  isLoading: boolean;
  userId: string;
  projectKey: string;
};

const LoadingSkeleton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 3 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

const PermissionsBadges = ({ permissions }: { permissions: string[] }) => {
  if (!permissions || permissions.length === 0)
    return <span className="text-medium-emphasis">-</span>;

  const visiblePermissions = permissions.slice(0, 4);
  const remainingCount = permissions.length - 4;

  return (
    <div className="flex flex-wrap gap-1">
      {visiblePermissions.map((permission, index) => (
        <Badge key={index} variant="secondary" className="text-xs">
          {permission}
        </Badge>
      ))}
      {remainingCount > 0 && (
        <TooltipProvider>
          <Tooltip>
            <TooltipTrigger>
              <Badge variant="outline" className="text-xs">
                +{remainingCount}
              </Badge>
            </TooltipTrigger>
            <TooltipContent className="flex max-w-[300px] flex-wrap gap-1 p-2">
              {permissions.slice(4).map((permission, index) => (
                <Badge key={index} variant="secondary" className="text-xs">
                  {permission}
                </Badge>
              ))}
            </TooltipContent>
          </Tooltip>
        </TooltipProvider>
      )}
    </div>
  );
};

export const UserMembershipsList = ({
  memberships,
  orgNameMap,
  isLoading,
  userId,
  projectKey,
}: UserMembershipsListProps) => {
  const [removeModalOpen, setRemoveModalOpen] = useState(false);
  const [editDrawerOpen, setEditDrawerOpen] = useState(false);
  const [selectedMembership, setSelectedMembership] = useState<IMembership | null>(null);

  const handleRemoveClick = (membership: IMembership) => {
    setSelectedMembership(membership);
    setRemoveModalOpen(true);
  };

  const handleEditClick = (membership: IMembership) => {
    setSelectedMembership(membership);
    setEditDrawerOpen(true);
  };

  const columns = useMemo<ColumnDef<IMembership>[]>(
    () => [
      {
        id: "name",
        accessorFn: (row) => orgNameMap.get(row.organizationId) || row.organizationId,
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Name</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="w-[150px] truncate">
            {orgNameMap.get(row.original.organizationId) || row.original.organizationId}
          </div>
        ),
      },
      {
        id: "roles",
        accessorFn: (row) => row.roles.join(", "),
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Roles</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="w-[150px]">
            {row.original.roles.length > 0 ? row.original.roles.join(", ") : "-"}
          </div>
        ),
      },
      {
        id: "permissions",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Permissions</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="min-w-[300px]">
            <PermissionsBadges
              permissions={
                (row.original as IMembership & { permissions?: string[] }).permissions || []
              }
            />
          </div>
        ),
      },
      {
        id: "actions",
        enableHiding: false,
        header: () => null,
        cell: ({ row }) => (
          <div className="flex justify-end">
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" className="h-8 w-8 p-0">
                  <EllipsisVertical className="h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem onSelect={() => handleEditClick(row.original)}>
                  <Settings className="mr-2 h-4 w-4" />
                  Configure
                </DropdownMenuItem>
                <DropdownMenuItem
                  className="text-destructive"
                  onSelect={() => handleRemoveClick(row.original)}
                >
                  <XCircle className="mr-2 h-4 w-4" />
                  Unassign User
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        ),
      },
    ],
    [orgNameMap],
  );

  const table = useReactTable({
    data: memberships,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  if (isLoading) {
    return <LoadingSkeleton />;
  }

  if (memberships.length === 0) {
    return (
      <div className="flex h-[100px] items-center justify-center text-medium-emphasis">
        No organization memberships found
      </div>
    );
  }

  return (
    <>
      <Table>
        <TableHeader>
          {table.getHeaderGroups().map((headerGroup) => (
            <TableRow key={headerGroup.id}>
              {headerGroup.headers.map((header) => (
                <TableHead key={header.id}>
                  {header.isPlaceholder
                    ? null
                    : flexRender(header.column.columnDef.header, header.getContext())}
                </TableHead>
              ))}
            </TableRow>
          ))}
        </TableHeader>
        <TableBody>
          {table.getRowModel().rows.map((row) => (
            <TableRow key={row.id}>
              {row.getVisibleCells().map((cell) => (
                <TableCell key={cell.id}>
                  {flexRender(cell.column.columnDef.cell, cell.getContext())}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>

      {selectedMembership && (
        <RemoveMembership
          open={removeModalOpen}
          onOpenChange={setRemoveModalOpen}
          membership={selectedMembership}
          organizationName={
            orgNameMap.get(selectedMembership.organizationId) || selectedMembership.organizationId
          }
          userId={userId}
          projectKey={projectKey}
        />
      )}

      {selectedMembership && (
        <EditMembership
          open={editDrawerOpen}
          onOpenChange={setEditDrawerOpen}
          membership={selectedMembership}
          organizationName={
            orgNameMap.get(selectedMembership.organizationId) || selectedMembership.organizationId
          }
          userId={userId}
          projectKey={projectKey}
        />
      )}
    </>
  );
};
