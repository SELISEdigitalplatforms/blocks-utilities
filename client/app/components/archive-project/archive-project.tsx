import { useState } from "react";
import { useNavigate } from "react-router";
import { Archive } from "lucide-react";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog, DialogTrigger } from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Button } from "@/components/ui-kits/button/button";
import { useProjectStore, useT } from "@seliseblocks/genesis-os";

import { useDisableProject } from "@blocks-identifier/hooks/use-project";
import { isErrorWithErrors } from "@/lib/error";

export const ArchivedProject = () => {
  const { t } = useT("utilities.shared");
  const navigate = useNavigate();
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync, isPending } = useDisableProject({ projectKey });
  const onClickHandler = async () => {
    try {
      const res = await mutateAsync({ projectKey });
      if (res.isSuccess) {
        showSuccessToast({ description: t("ARCHIVE_PROJECT.PROJECT_DELETED_SUCCESSFULLY") });
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
          <span className="sr-only sm:not-sr-only sm:ml-2">{t("common:DELETE")}</span>
        </Button>
      </DialogTrigger>
      <ConfirmationModal
        onCancel={() => setOpen(false)}
        onConfirm={onClickHandler}
        data={{
          dialogTitle: t("ARCHIVE_PROJECT.DELETE_THIS_ENVIRONMENT"),
          dialogSubtitle: (
            <>
              <p>{t("ARCHIVE_PROJECT.ARE_YOU_SURE_YOU_WANT")}</p>
              <p>
                {t("ARCHIVE_PROJECT.THIS_WILL_PERMANENTLY_DELETE_THE")}
              </p>
            </>
          ),
          confirmButton: t("common:DELETE"),
        }}
        buttonState={{ confirm: { disable: isPending } }}
      />
    </Dialog>
  );
};
