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
import { useDisableMfa } from "@blocks-idp/mfa/hooks/use-mfa-config";
import { useContext } from "react";
import { profileMfaContext } from "../profile-mfa";

export const UserMFAConfirmationDisable = () => {
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
      showSuccessToast({ description: "MFA disabled successfully" });
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
          <DialogTitle>Disable MFA?</DialogTitle>
          <DialogDescription>
            Are you sure you want to disable Multi-Factor Authentication (MFA) for this account?
            Disabling MFA may reduce the security of this account.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter className="mt-4 flex flex-row gap-2">
          <DialogTrigger asChild>
            <Button variant="outline" size="sm" disabled={isPending}>
              Cancel
            </Button>
          </DialogTrigger>
          <Button size="sm" onClick={onClickHandler} disabled={isPending}>
            {isPending ? "Processing" : "Yes"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
