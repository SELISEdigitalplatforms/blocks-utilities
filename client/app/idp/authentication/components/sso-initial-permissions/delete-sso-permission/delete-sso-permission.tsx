import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog, DialogTrigger } from "@/components/ui-kits/dialog/dialog";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { X } from "lucide-react";
import { useState } from "react";
import { useT } from "@seliseblocks/genesis-os";

type DeleteUserPermissionProps = {
  permission: IPermission;
  onDelete: (data: IPermission) => void;
};

export const DeleteSSOPermission = ({ permission, onDelete }: DeleteUserPermissionProps) => {
  const { t } = useT("utilities.idp.authentication");
  const [open, setOpen] = useState<boolean>(false);

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <X className="h-4 w-4" />
      </DialogTrigger>
      <ConfirmationModal
        data={{
          dialogTitle: t("DELETE_SSO_PERMISSION.REMOVE_PERMISSION"),
          dialogSubtitle: t("DELETE_SSO_PERMISSION.ARE_YOU_SURE_YOU_WANT"),
        }}
        onConfirm={() => onDelete(permission)}
        onCancel={() => setOpen(false)}
      />
    </Dialog>
  );
};
