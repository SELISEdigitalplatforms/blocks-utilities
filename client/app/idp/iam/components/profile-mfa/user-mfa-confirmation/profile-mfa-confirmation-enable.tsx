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
import { showErrorToast, showSuccessToast, toast } from "@/hooks/use-toast";
import { useConfigureUserMFA } from "@blocks-idp/mfa/hooks/use-mfa-config";
import { useContext, useState } from "react";
import { ProfileMFAMethodList } from "./profile-mfa-methods-list";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { isErrorWithErrors } from "@/lib/error";
import { profileMfaContext } from "../profile-mfa";

export const UserMFAConfirmationEnable = () => {
  const { projectKey, userId, showVerifyModal } = useContext(profileMfaContext);
  const [open, setOpen] = useState<boolean>(false);

  const [type, setType] = useState(0);
  const { isPending, mutateAsync } = useConfigureUserMFA({ id: userId, projectKey });
  const { data: userData, isLoading, isFetching } = useGetUserById({ id: userId, projectKey });

  const onClickHandler = async () => {
    try {
      const res = await mutateAsync({
        mfaEnabled: true,
        projectKey,
        userId,
        userMfaType: type,
      });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({ description: "MFA enabled successfully" });
      setOpen(false);
      showVerifyModal(type);
    } catch (error) {
      if (isErrorWithErrors(error)) showErrorToast({ errors: error.errors });
    }
  };

  const onTriggerHandler = () => {
    if (!userData?.data.isVarified) {
      return toast({
        variant: "info",
        title: "Info",
        description: `Please verify the user first`,
      });
    }
    if (!userData?.data.active) {
      return toast({
        variant: "info",
        title: "Info",
        description: `Please active the user first`,
      });
    }
    return setOpen(true);
  };

  const onOpenChangeHandler = (isOpen: boolean) => {
    setOpen(isOpen);
    if (!isOpen) setType(0);
  };
  return (
    <Dialog open={open} onOpenChange={onOpenChangeHandler}>
      <Button variant="outline" onClick={onTriggerHandler} size="sm">
        Enable
      </Button>

      <DialogContent>
        <DialogHeader>
          <DialogTitle>Enable MFA?</DialogTitle>
          <DialogDescription>Select the method of MFA for this user.</DialogDescription>
        </DialogHeader>
        <ProfileMFAMethodList selected={type} setSelected={setType} />
        <DialogFooter className="mt-4 flex flex-row gap-2">
          <DialogTrigger asChild>
            <Button size="sm" variant="outline" disabled={isPending}>
              Cancel
            </Button>
          </DialogTrigger>
          <Button
            size="sm"
            onClick={onClickHandler}
            disabled={isPending || type === 0 || isLoading || isFetching}
          >
            {isPending ? "Saving" : "Save"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
