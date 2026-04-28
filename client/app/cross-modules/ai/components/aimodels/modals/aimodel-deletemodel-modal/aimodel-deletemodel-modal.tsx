import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useProjectStore } from "@/store/useProjectStore";
import { useDeleteModel } from "@blocks-ai/hooks/use-aimodel";

type DeleteModelProps = {
  modelId: string;
  open: boolean;
  onOpenChange: (value: boolean) => void;
};

export const DeleteModel = ({ modelId, open, onOpenChange }: DeleteModelProps) => {
  const project_key = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync } = useDeleteModel();

  const confirmHandler = async () => {
    try {
      if (!modelId) {
        showErrorToast({ errors: "Something went wrong" });
        return onOpenChange(false);
      }
      const res = await mutateAsync({ modelId, project_key });
      if (!res?.is_success) {
        return showErrorToast({ errors: (res as unknown as { error?: string; detail?: string }).error ?? (res as unknown as { detail?: string }).detail });
      }
      showSuccessToast({ description: "Model deleted successfully" });
      onOpenChange(false);
    } catch (error) {
      if (isErrorWithErrors(error)) {
        return showErrorToast({ errors: error.errors });
      }
      return showErrorToast({ errors: "Something went wrong" });
    }
  };

  return (
    <Dialog open={open} onOpenChange={(value) => { if (!value) onOpenChange(false); }}>
      <ConfirmationModal
        data={{
          dialogTitle: "Delete Model",
          dialogSubtitle: "Are you sure you want to delete the model?",
        }}
        onConfirm={confirmHandler}
        onCancel={() => onOpenChange(false)}
      />
    </Dialog>
  );
};
