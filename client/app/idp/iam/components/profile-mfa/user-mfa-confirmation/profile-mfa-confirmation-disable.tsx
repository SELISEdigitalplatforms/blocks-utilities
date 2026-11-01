import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useDisableMfa } from "@/idp/mfa/hooks/use-mfa-config";
import { useContext } from "react";
import { profileMfaContext } from "../profile-mfa";
import { useT } from "@seliseblocks/genesis-os";
export const UserMFAConfirmationDisable = () => {
  const { t } = useT("utilities.idp");
  const { projectKey, userId, isDisableModalOpen, setIsDisableModalOpen } =
    useContext(profileMfaContext);
  const { isPending, mutateAsync } = useDisableMfa({ id: userId, projectKey });
  const onClickHandler = async () => {
    try {
      const res = await mutateAsync({
        projectKey,
        userId,
      });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({ description: t("PROFILE_MFA_CONFIRMATION_DISABLE.MFA_DISABLED_SUCCESSFULLY") });
      setIsDisableModalOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      }
    }
  };
  return (
    <Dialog open={isDisableModalOpen} onOpenChange={setIsDisableModalOpen}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("PROFILE_MFA_CONFIRMATION_DISABLE.DISABLE_MFA")}</DialogTitle>
          <DialogDescription>
            {t("PROFILE_MFA_CONFIRMATION_DISABLE.ARE_YOU_SURE_YOU_WANT")}
          </DialogDescription>
        </DialogHeader>
        <DialogFooter className="mt-4 flex flex-row gap-2">
          <DialogTrigger asChild>
            <Button variant="outline" size="sm" disabled={isPending}>
              {t("common:CANCEL")}
            </Button>
          </DialogTrigger>
          <Button size="sm" onClick={onClickHandler} disabled={isPending}>
            {isPending ? t("PROFILE_MFA_CONFIRMATION_DISABLE.PROCESSING") : t("common:YES")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
