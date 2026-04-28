import { useMemo, useState, useCallback } from "react";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  ColumnDef,
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  useReactTable,
} from "@tanstack/react-table";
import { tableColumns } from "./aimodel-columns";
import { IModelInfo } from "@blocks-ai/types/aimodel.service.type";
import { DeleteModel } from "@blocks-ai/components/aimodels/modals/aimodel-deletemodel-modal/aimodel-deletemodel-modal";
import { ModelEditKeyModal } from "@blocks-ai/components/aimodels/modals/aimodel-editkey-modal/aimodel-editkey-modal";
import { CustomModelEditKeyModal } from "@blocks-ai/components/aimodels/modals/aimodel-editkey-modal-custom/aimodel-editkey-modal-custom";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useValidateModel } from "@blocks-ai/hooks/use-aimodel";

type AIModelsTableProps = {
  custom: boolean;
  models: IModelInfo[];
  isLoading: boolean;
};

const LoadingSkeleton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 5 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

export const AIModelsTable = ({ custom, models, isLoading }: AIModelsTableProps) => {
  const [deleteTarget, setDeleteTarget] = useState<IModelInfo | null>(null);
  const [editTarget, setEditTarget] = useState<IModelInfo | null>(null);
  const [editKeyModalOpen, setEditKeyModalOpen] = useState(false);
  const [actionMenuRowId, setActionMenuRowId] = useState<string | null>(null);
  const [validatingRowId, setValidatingRowId] = useState<string | null>(null);

  const { mutate: validateModel, isPending: isValidating } = useValidateModel();

  const handleDeleteClick = useCallback((model: IModelInfo) => {
    setDeleteTarget(model);
  }, []);

  const handleEditClick = useCallback((model: IModelInfo) => {
    setEditTarget(model);
    setEditKeyModalOpen(true);
  }, []);

  const handleValidateClick = useCallback(
    (model: IModelInfo) => {
      setValidatingRowId(model._id);
      validateModel(
        { modelId: model._id, project_key: model.ProjectKey },
        {
          onSuccess: (res) => {
            const isValid = !!res.valid?.valid;
            const messageFromBackend = res.valid?.message || res.message || "Validation completed.";
            if (isValid) showSuccessToast({ description: messageFromBackend });
            else showErrorToast({ errors: messageFromBackend });
            setValidatingRowId(null);
          },
          onError: (error) => {
            const fallbackMessage =
              error instanceof Error ? error.message : "Failed to validate model. Please try again.";
            showErrorToast({ errors: fallbackMessage });
            setValidatingRowId(null);
          },
        },
      );
    },
    [validateModel],
  );

  const columns = useMemo<ColumnDef<IModelInfo>[]>(
    () =>
      tableColumns(custom, {
        onEdit: handleEditClick,
        onDelete: handleDeleteClick,
        onValidate: handleValidateClick,
        isValidating,
        validatingRowId,
        onRowMenuOpenChange: (rowId, open) => {
          setActionMenuRowId(open ? rowId : null);
        },
      }),
    [custom, handleEditClick, handleDeleteClick, handleValidateClick, isValidating, validatingRowId],
  );

  const table = useReactTable({
    data: models,
    columns,
    enableRowSelection: true,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
  });

  if (isLoading) return <LoadingSkeleton />;

  const getClass = (columnId: string): string => {
    if (columnId === "actions" || columnId === "validate") return "w-[10%]";
    return "w-[20%]";
  };

  return (
    <div>
      <Table className="w-full table-fixed">
        <TableHeader>
          <TableRow>
            {table.getHeaderGroups().map((headerGroup) =>
              headerGroup.headers.map((header) => (
                <TableHead key={header.id} className={`${getClass(header.column.id)} truncate`}>
                  {flexRender(header.column.columnDef.header, header.getContext())}
                </TableHead>
              )),
            )}
          </TableRow>
        </TableHeader>
        <TableBody>
          {!models.length ? (
            <TableRow>
              <TableCell colSpan={columns.length} className="h-24 text-center text-muted-foreground">
                No models found.
              </TableCell>
            </TableRow>
          ) : (
            table.getRowModel().rows.map((row) => (
              <TableRow
                key={row.id}
                className={`group cursor-pointer ${actionMenuRowId === row.original._id ? "bg-muted" : ""}`}
                isHoverable
              >
                {row.getVisibleCells().map((cell) => (
                  <TableCell key={cell.id} className={`${getClass(cell.column.id)} truncate`}>
                    {flexRender(cell.column.columnDef.cell, cell.getContext())}
                  </TableCell>
                ))}
              </TableRow>
            ))
          )}
        </TableBody>
      </Table>

      {deleteTarget && (
        <DeleteModel
          modelId={deleteTarget._id}
          open={!!deleteTarget}
          onOpenChange={(open) => {
            if (!open) setDeleteTarget(null);
          }}
        />
      )}

      {editTarget &&
        (custom ? (
          <CustomModelEditKeyModal
            editKeyModalOpen={editKeyModalOpen}
            setEditKeyModalOpen={(open) => {
              setEditKeyModalOpen(open);
              if (!open) setEditTarget(null);
            }}
            model={editTarget}
          />
        ) : (
          <ModelEditKeyModal
            modelOptions={[
              { model: editTarget.ModelName || "", goodName: editTarget.DisplayName || "" },
            ]}
            editKeyModalOpen={editKeyModalOpen}
            setEditKeyModalOpen={(open) => {
              setEditKeyModalOpen(open);
              if (!open) setEditTarget(null);
            }}
            model={editTarget}
          />
        ))}
    </div>
  );
};
