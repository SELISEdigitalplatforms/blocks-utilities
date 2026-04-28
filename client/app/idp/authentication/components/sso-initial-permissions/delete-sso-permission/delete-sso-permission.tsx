import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog, DialogTrigger } from "@/components/ui-kits/dialog/dialog";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { X } from "lucide-react";
import { useState } from "react";

type DeleteUserPermissionProps = {
  permission: IPermission;
  onDelete: (data: IPermission) => void;
};

export const DeleteSSOPermission = ({ permission, onDelete }: DeleteUserPermissionProps) => {
  const [open, setOpen] = useState<boolean>(false);

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <X className="h-4 w-4" />
      </DialogTrigger>
      <ConfirmationModal
        data={{
          dialogTitle: "Remove Permission",
          dialogSubtitle: "Are you sure you want to remove the permission?",
        }}
        onConfirm={() => onDelete(permission)}
        onCancel={() => setOpen(false)}
      />
    </Dialog>
  );
};
