import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";

import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useAccountRecover } from "@blocks-idp/iam/hooks/use-account";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";

type UserResetPasswordProps = {
  userId: string;
  projectKey: string;
  open: boolean;
  setOpen: (value: boolean) => void;
};

export const UserResetPassword = ({
  userId,
  projectKey,
  open,
  setOpen,
}: UserResetPasswordProps) => {
  const { data } = useGetUserById({ projectKey, id: userId });
  const { mutateAsync, isPending } = useAccountRecover();

  const onClickHandler = async () => {
    try {
      const email = data?.data.email;
      if (!email) {
        throw Error("Email is not provided yet");
      }
      const res = await mutateAsync({
        projectKey,
        email: data?.data.email,
        captchaCode: "",
      });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({
        description: "Password reset email has been sent. Please check your email",
      });
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  };

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <ConfirmationModal
        onCancel={() => setOpen(false)}
        onConfirm={onClickHandler}
        data={{
          dialogTitle: "Reset password",
          dialogSubtitle: "Are you sure you want to reset the password for this user?",
          confirmButton: "Reset",
        }}
        buttonState={{
          confirm: { disable: isPending },
        }}
      />
    </Dialog>
  );
};
