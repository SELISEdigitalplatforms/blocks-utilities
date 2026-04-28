import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog, DialogTrigger } from "@/components/ui-kits/dialog/dialog";
import { IRole } from "@blocks-idp/iam/models/role";
import { X } from "lucide-react";
import { useState } from "react";

type DeleteUserRoleProps = {
  role: IRole;
  onDelete: (role: IRole) => void;
};

export const DeleteSSORole = ({ role, onDelete }: DeleteUserRoleProps) => {
  const [open, setOpen] = useState<boolean>(false);

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <X className="h-4 w-4" />
      </DialogTrigger>
      <ConfirmationModal
        data={{
          dialogTitle: "Remove Role",
          dialogSubtitle: "Are you sure you want to remove the role?",
        }}
        onConfirm={() => onDelete(role)}
        onCancel={() => setOpen(false)}
      />
    </Dialog>
  );
};
