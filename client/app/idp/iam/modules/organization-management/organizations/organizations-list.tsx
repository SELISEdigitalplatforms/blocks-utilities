

import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useMemo, useState } from "react";
import { IOrganization } from "@blocks-idp/iam/models/organization";
// import { FilterControls } from "@/components/filter-toolbar";
import { useOrganizationsSortQueryParams } from "./organizations-filter-toolbar";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { EllipsisVertical, Pencil, Power, PowerOff } from "lucide-react";
import { UpdateOrganization } from "../update-organization";
import { ToggleOrganizationStatus } from "../toggle-organization-status";
import { useNavigate } from "react-router-dom";

type OrganizationTableProps = {
  organizations: IOrganization[];
  isLoading: boolean;
};

const LoadingSkeleton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 5 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

type OrganizationActionsProps = {
  organization: IOrganization;
};

const OrganizationActions = ({ organization }: OrganizationActionsProps) => {
  const [isRenameModalOpen, setIsRenameModalOpen] = useState(false);
  const [isToggleStatusModalOpen, setIsToggleStatusModalOpen] = useState(false);

  const handleClick = (e: React.MouseEvent) => {
    e.stopPropagation();
  };

  return (
    <div onClick={handleClick}>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" className="px-2">
            <EllipsisVertical className="h-4 w-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem className="gap-2" onSelect={() => setIsRenameModalOpen(true)}>
            <Pencil className="aspect-square w-4" />
            <span>Rename</span>
          </DropdownMenuItem>
          <DropdownMenuItem className="gap-2" onSelect={() => setIsToggleStatusModalOpen(true)}>
            {organization.isEnable ? (
              <>
                <PowerOff className="aspect-square w-4" />
                <span>Disable</span>
              </>
            ) : (
              <>
                <Power className="aspect-square w-4" />
                <span>Enable</span>
              </>
            )}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
      <Dialog open={isRenameModalOpen} onOpenChange={setIsRenameModalOpen}>
        <UpdateOrganization organization={organization} isOpen={isRenameModalOpen} />
      </Dialog>
      <Dialog open={isToggleStatusModalOpen} onOpenChange={setIsToggleStatusModalOpen}>
        <ToggleOrganizationStatus
          organization={organization}
          onClose={() => setIsToggleStatusModalOpen(false)}
        />
      </Dialog>
    </div>
  );
};

export const OrganizationsList = ({ organizations, isLoading }: OrganizationTableProps) => {
  const { sortQueryParams, setSortQueryParams } = useOrganizationsSortQueryParams();
  const navigate = useNavigate();

  const handleRowClick = (e: React.MouseEvent<HTMLTableRowElement>, itemId: string) => {
    const target = e.target as HTMLElement;
    if (target.closest("[data-actions]")) return;
    navigate(`/services/iam/organization-detail/${itemId}`);
  };

  const columns = useMemo<ColumnDef<IOrganization>[]>(
    () => [
      {
        id: "name",
        accessorFn: (row) => `${row.name}`.trim(),
        header: () => (
          // <FilterControls.SortHeader
          //   label="Name"
          //   id="Name"
          //   value={sortQueryParams}
          //   onChange={setSortQueryParams}
          // />
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Name</span>
          </div>
        ),
        cell: (organization) => (
          <div>
            <span>{organization.row.original.name}</span>
          </div>
        ),
      },
      {
        id: "status",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Status</span>
          </div>
        ),
        cell: (organization) => (
          <div>
            <Badge
              className="w-fit"
              variant={organization.row.original.isEnable ? "success" : "error"}
            >
              {organization.row.original.isEnable ? "Active" : "Disabled"}
            </Badge>
          </div>
        ),
      },
      {
        id: "actions",
        header: () => (
          <div className="flex items-center justify-end">
            <span className="font-bold text-medium-emphasis"></span>
          </div>
        ),
        cell: (organization) => (
          <div className="flex justify-end" data-actions>
            {organization.row.original.itemId !== "default" && (
              <OrganizationActions organization={organization.row.original} />
            )}
          </div>
        ),
      },
    ],
    [sortQueryParams, setSortQueryParams],
  );

  const table = useReactTable({
    data: organizations,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  if (isLoading) {
    return <LoadingSkeleton />;
  }

  if (organizations.length === 0) {
    return (
      <div className="flex h-[200px] items-center justify-center text-medium-emphasis">
        No organizations found
      </div>
    );
  }

  return (
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
          <TableRow
            key={row.id}
            className="cursor-pointer"
            onClick={(e) => handleRowClick(e, row.original.itemId)}
            isHoverable
          >
            {row.getVisibleCells().map((cell) => (
              <TableCell key={cell.id}>
                {flexRender(cell.column.columnDef.cell, cell.getContext())}
              </TableCell>
            ))}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
};
