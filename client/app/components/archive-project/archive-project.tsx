import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Archive } from "lucide-react";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog, DialogTrigger } from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Button } from "@/components/ui-kits/button/button";
import { useProjectStore } from "@/store/useProjectStore";
import { useDisableProject } from "@/hooks/use-project";
import { isErrorWithErrors } from "@/lib/error";

export const ArchivedProject = () => {
  const navigate = useNavigate();
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync, isPending } = useDisableProject({ projectKey });
  const onClickHandler = async () => {
    try {
      const res = await mutateAsync({ projectKey });
      if (res.isSuccess) {
        showSuccessToast({ description: "Project deleted successfully" });
        navigate("/console");
      } else {
        showErrorToast({ errors: res.errors });
      }
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      }
    }
  };
  const [open, setOpen] = useState<boolean>(false);
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="outline">
          <Archive className="h-4 w-4" />
          <span className="sr-only sm:not-sr-only sm:ml-2">Delete</span>
        </Button>
      </DialogTrigger>
      <ConfirmationModal
        onCancel={() => setOpen(false)}
        onConfirm={onClickHandler}
        data={{
          dialogTitle: "Delete this environment?",
          dialogSubtitle: (
            <>
              <p>Are you sure you want to delete this environment?</p>
              <p>
                This will permanently delete the environment and you&apos;ll need to contact support
                to recover it.
              </p>
            </>
          ),
          confirmButton: "Delete",
        }}
        buttonState={{ confirm: { disable: isPending } }}
      />
    </Dialog>
  );
};
