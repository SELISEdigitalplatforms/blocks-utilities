import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Button } from "@/components/ui-kits/button/button";
import { EllipsisVertical } from "lucide-react";
import { IModelInfo } from "@blocks-ai/types/aimodel.service.type";

type AIModelRowActionsDropdownProps = {
  model: IModelInfo;
  onEdit: (model: IModelInfo) => void;
  onDelete: (model: IModelInfo) => void;
  onOpenChange?: (open: boolean) => void;
};

export const AIModelRowActionsDropdown = ({
  model,
  onEdit,
  onDelete,
  onOpenChange,
}: AIModelRowActionsDropdownProps) => {
  return (
    <DropdownMenu onOpenChange={onOpenChange}>
      <DropdownMenuTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className="h-7 w-7 rounded-full p-0"
          onClick={(e) => e.stopPropagation()}
        >
          <EllipsisVertical className="h-4 w-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem
          onClick={(e) => {
            e.stopPropagation();
            onEdit(model);
          }}
        >
          Edit
        </DropdownMenuItem>
        <DropdownMenuItem
          className="!text-error"
          onClick={(e) => {
            e.stopPropagation();
            onDelete(model);
          }}
        >
          Delete
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
};
