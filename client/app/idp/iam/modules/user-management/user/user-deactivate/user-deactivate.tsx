import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useProjectStore } from "@/store/useProjectStore";
import { useAccountDeactivate } from "@blocks-idp/iam/hooks/use-account";
import { isErrorWithErrors } from "@/lib/error";

type UserDeactivateProps = {
  userId: string;
  open: boolean;
  setOpen: (open: boolean) => void;
};

export const UserDeactivate = ({
  userId,
  open,
  setOpen,
}: UserDeactivateProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync, isPending } = useAccountDeactivate();
  const onClickHandler = async () => {
    try {
      const res = await mutateAsync({ projectKey: tenantId, userId });
      if (res.isSuccess) {
        showSuccessToast(
          { description: "User has been deactivated successfully." }
        );
      }
      else { showErrorToast({ errors: res.errors }); }
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      }
      else {
        showErrorToast({ errors: "Something went wrong" });
      }
    } finally {
      setOpen(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <ConfirmationModal
        onCancel={() => setOpen(false)}
        onConfirm={onClickHandler}
        data={{
          dialogTitle: "Confirmation",
          dialogSubtitle: "Are you sure you want to deactivate this user?",
          confirmButton: "Deactivate",
        }}
        buttonState={{
          confirm: { disable: isPending },
        }}
      />
    </Dialog>
  );
};
