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

type UserDisableMFAProps = {
  projectKey: string;
  userId: string;
  open: boolean;
  setOpen: (open: boolean) => void;
};

export const UserDisableMFA = ({ userId, projectKey, open, setOpen }: UserDisableMFAProps) => {
  const { isPending, mutateAsync } = useDisableMfa({ id: userId, projectKey });
  const onClickHandler = async () => {
    try {
      const res = await mutateAsync({
        projectKey,
        userId,
      });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({ description: "MFA disabled successfully" });
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      }
    }
  };

  return (
    <Dialog open={open} onOpenChange={setOpen}>
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
