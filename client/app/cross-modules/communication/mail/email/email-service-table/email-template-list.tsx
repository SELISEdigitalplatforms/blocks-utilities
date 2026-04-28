

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
import { IEmailConfig, IEmailTemplate } from "@blocks-communication/mail/models/email";
import { checkValidDate, formatDate, parseDateString } from "@/lib/utils";
import { FilterControls } from "@/components/filter-toolbar";
import { useTemplatesSortQueryParams } from "./template-filter-toolbar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Button } from "@/components/ui-kits/button/button";
import { AlignLeft, Copy, MoreVertical, Trash } from "lucide-react";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { toast } from "@/hooks/use-toast";
import {
  useCloneTemplate,
  useDeleteEmailTemplate,
} from "@blocks-communication/mail/hooks/use-email-template";
import { useProjectStore } from "@/store/useProjectStore";
import { useNavigate } from "react-router-dom";

type EmailTemplateListProps = {
  templates: IEmailTemplate[];
  isLoading: boolean;
  emailConfigsData: IEmailConfig[];
  onRowClick: (emailId: number | string) => void;
};

const LoadingSkeleton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 5 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

export const EmailTemplateList = ({
  templates,
  isLoading,
  emailConfigsData,
  onRowClick,
}: EmailTemplateListProps) => {
  const navigate = useNavigate();
  const { sortQueryParams, setSortQueryParams } = useTemplatesSortQueryParams();
  const { isPending: isClonePending, mutateAsync: cloneMailTemplate } = useCloneTemplate();
  const { isPending: isDeletePending, mutateAsync: deleteMailTemplate } = useDeleteEmailTemplate();
  const [isCloneDialogOpen, setIsCloneDialogOpen] = useState(false);
  const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false);
  const [selectedTemplateData, setSelectedTemplateData] = useState<IEmailTemplate | null>(null);
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  const cloneEmailTemplate = (rowData: IEmailTemplate) => {
    setSelectedTemplateData(rowData);
    setIsCloneDialogOpen(true);
  };
  const DeleteEmailTemplate = (rowData: IEmailTemplate) => {
    setSelectedTemplateData(rowData);
    setIsDeleteDialogOpen(true);
  };

  const onConfirmDeleteTemplate = async () => {
    try {
      const payload = {
        projectKey: tenantId,
        itemId: selectedTemplateData?.itemId ?? "",
      };
      const res = await deleteMailTemplate(payload);
      if (res?.isSuccess) {
        toast({
          variant: "success",
          title: "Success",
          description: "Deleted template successfully",
        });
        setIsDeleteDialogOpen(false);
      } else {
        toast({
          variant: "destructive",
          title: "Error",
          description: JSON.stringify(res?.errors),
        });
      }
    } catch (error) {
      toast({
        variant: "destructive",
        title: "Error",
        description: JSON.stringify(error),
      });
    }
  };

  const onConfirmCloneTemplate = async () => {
    try {
      const payload = {
        projectKey: tenantId,
        itemId: selectedTemplateData?.itemId ?? "",
      };
      const res = await cloneMailTemplate(payload);
      if (res?.isSuccess) {
        toast({
          variant: "success",
          title: "Success",
          description: "Cloned template successfully",
        });
        setIsCloneDialogOpen(false);
        navigate(`/utilities/email/communications/${res?.itemId}`);
      } else {
        toast({
          variant: "destructive",
          title: "Error",
          description: JSON.stringify(res?.errors),
        });
      }
    } catch (error) {
      toast({
        variant: "destructive",
        title: "Error",
        description: JSON.stringify(error),
      });
    }
  };
  const columns = useMemo<ColumnDef<IEmailTemplate>[]>(
    () => [
      {
        accessorKey: "name",
        header: () => (
          <FilterControls.SortHeader
            label="Name"
            id="Name"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: ({ row }) => <div className="truncate">{row.getValue("name")}</div>,
      },
      {
        accessorKey: "MailConfigurationId",
        header: "Configuration",
        cell: ({ row }) => (
          <div className="truncate">
            {
              emailConfigsData?.find((config) => config.itemId === row.original.mailConfigurationId)
                ?.name
            }
          </div>
        ),
      },
      {
        accessorKey: "templateSubject",
        header: () => (
          <FilterControls.SortHeader
            label="Subject"
            id="TemplateSubject"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: ({ row }) => <div className="truncate">{row.getValue("templateSubject")}</div>,
      },
      {
        accessorKey: "lastUpdatedDate",
        header: () => (
          <FilterControls.SortHeader
            label="Last Modified"
            id="LastUpdatedDate"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: ({ row }) => {
          const dateValue = parseDateString(row.getValue("lastUpdatedDate"));
          return checkValidDate(dateValue) ? formatDate(dateValue) : "-";
        },
      },
      {
        id: "actions",
        enableHiding: false,
        cell: ({ row, table }) => {
          // const meta = table.options.meta as ExtendedMeta;
          return (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" className="h-8 w-8 p-0">
                  <span className="sr-only">Open menu</span>
                  <MoreVertical className="h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem
                  className="cursor-pointer hover:no-underline"
                  onClick={() => onRowClick(row.original.itemId)}
                >
                  <AlignLeft className="mr-2 h-4 w-4" />
                  <span>View details</span>
                </DropdownMenuItem>
                <DropdownMenuItem
                  className="cursor-pointer hover:no-underline"
                  onClick={(e) => {
                    e.stopPropagation();
                    cloneEmailTemplate(row.original);
                  }}
                >
                  <Copy className="mr-2 h-4 w-4" />
                  <span>Clone Template</span>
                </DropdownMenuItem>
                {row.original.generatedBy !== "Tenant" && (
                  <DropdownMenuItem
                    className="cursor-pointer text-error hover:no-underline"
                    onClick={(e) => {
                      e.stopPropagation();
                      DeleteEmailTemplate(row.original);
                    }}
                  >
                    <Trash className="mr-2 h-4 w-4" />
                    <span>Delete</span>
                  </DropdownMenuItem>
                )}
                {/* <DropdownMenuItem className="cursor-pointer text-error" onClick={(e) => {
                      e.stopPropagation();
                      DeleteEmailTemplate(row.original);
                    }}>
                      <Trash className="mr-2 h-4 w-4" />
                      <span>Delete</span>
                    </DropdownMenuItem> */}
              </DropdownMenuContent>
            </DropdownMenu>
          );
        },
      },
    ],
    [sortQueryParams, setSortQueryParams, emailConfigsData],
  );

  const table = useReactTable({
    data: templates,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  if (isLoading) return <LoadingSkeleton />;

  return (
    <Table className="text-sm">
      <TableHeader>
        {table.getHeaderGroups().map((headerGroup) => (
          <TableRow key={headerGroup.id}>
            {headerGroup.headers.map((header) => (
              <TableHead key={header.id} className={header.id === "actions" ? "w-10" : ""}>
                {header.isPlaceholder
                  ? null
                  : flexRender(header.column.columnDef.header, header.getContext())}
              </TableHead>
            ))}
          </TableRow>
        ))}
      </TableHeader>
      <TableBody>
        {table.getRowModel().rows.length ? (
          table.getRowModel().rows.map((row) => (
            <TableRow
              key={row.id}
              className="cursor-pointer hover:no-underline"
              onClick={() => onRowClick(row.original.itemId)}
              isHoverable
            >
              {row.getVisibleCells().map((cell) => (
                <TableCell
                  key={cell.id}
                  className={cell.column.id === "actions" ? "text-right" : ""}
                >
                  {flexRender(cell.column.columnDef.cell, cell.getContext())}
                </TableCell>
              ))}
            </TableRow>
          ))
        ) : (
          <TableRow>
            <TableCell colSpan={columns.length} className="text-center">
              No templates found.
            </TableCell>
          </TableRow>
        )}
      </TableBody>
      <Dialog open={isCloneDialogOpen} onOpenChange={setIsCloneDialogOpen}>
        {!isLoading && selectedTemplateData && (
          <ConfirmationModal
            onCancel={() => {}}
            onConfirm={() => onConfirmCloneTemplate()}
            data={{
              dialogTitle: "Confirmation",
              dialogSubtitle: `Are you sure you want to clone the ${selectedTemplateData?.name} template?`,
            }}
            buttonState={{ confirm: { disable: isClonePending } }}
          />
        )}
      </Dialog>
      <Dialog open={isDeleteDialogOpen} onOpenChange={setIsDeleteDialogOpen}>
        {!isLoading && selectedTemplateData && (
          <ConfirmationModal
            onCancel={() => {}}
            onConfirm={() => onConfirmDeleteTemplate()}
            data={{
              dialogTitle: "Confirmation",
              dialogSubtitle: `Are you sure you want to delete the ${selectedTemplateData?.name} template?`,
            }}
            buttonState={{ confirm: { disable: isDeletePending } }}
          />
        )}
      </Dialog>
    </Table>
  );
};
