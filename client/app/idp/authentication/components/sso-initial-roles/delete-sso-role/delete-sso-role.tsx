import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog, DialogTrigger } from "@/components/ui-kits/dialog/dialog";
import { IRole } from "@blocks-idp/iam/models/role";
import { X } from "lucide-react";
import { useState } from "react";
import { useT } from "@seliseblocks/genesis-os";

type DeleteUserRoleProps = {
  role: IRole;
  onDelete: (role: IRole) => void;
};

export const DeleteSSORole = ({ role, onDelete }: DeleteUserRoleProps) => {
  const { t } = useT("utilities.idp.authentication");
  const [open, setOpen] = useState<boolean>(false);

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <X className="h-4 w-4" />
      </DialogTrigger>
      <ConfirmationModal
        data={{
          dialogTitle: t("DELETE_SSO_ROLE.REMOVE_ROLE"),
          dialogSubtitle: t("DELETE_SSO_ROLE.ARE_YOU_SURE_YOU_WANT"),
        }}
        onConfirm={() => onDelete(role)}
        onCancel={() => setOpen(false)}
      />
    </Dialog>
  );
};
