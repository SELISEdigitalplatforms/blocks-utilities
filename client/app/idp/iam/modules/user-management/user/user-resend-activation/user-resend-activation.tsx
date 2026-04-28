import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { toast } from "@/hooks/use-toast";
import { useProjectStore } from "@/store/useProjectStore";
import { useAccountResendActivation } from "@blocks-idp/iam/hooks/use-account";

type UserResendActivationMailProps = {
  userId: string;
  open: boolean;
  setOpen: (open: boolean) => void;
};

export const UserResendActivationMail = ({
  userId,
  open,
  setOpen,
}: UserResendActivationMailProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync, isPending } = useAccountResendActivation();
  const onClickHandler = async () => {
    try {
      const res = await mutateAsync({ projectKey: tenantId, userId });
      if (res.isSuccess) {
        toast({
          variant: "success",
          title: "Success",
          description: `Activation email has been resent successfully`,
        });
      }
    } catch (error) {
      toast({
        variant: "destructive",
        title: "Error",
        description: `Something went wrong | ${JSON.stringify(error)}`,
      });
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
          dialogSubtitle: "Do you want to resend the activation email to this user? ",
          confirmButton: "Resend",
        }}
        buttonState={{
          confirm: { disable: isPending },
        }}
      />
    </Dialog>
  );
};
