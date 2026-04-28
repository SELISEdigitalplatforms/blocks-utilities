import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useProjectStore } from "@/store/useProjectStore";
import { useUpdateSsoCredentialStatus } from "@blocks-idp/authentication/hooks/use-sso";
import { ISsoProviderConfigurationWithMeta } from "@blocks-idp/authentication/models/sso.model";

type SSoProviderStatusToggleProps = {
  open: boolean;
  setOpen: (value: boolean) => void;
  configuration: ISsoProviderConfigurationWithMeta;
};

export const SSoProviderStatusToggle = ({
  open,
  setOpen,
  configuration,
}: SSoProviderStatusToggleProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync } = useUpdateSsoCredentialStatus();

  const udpateStatus = async () => {
    try {
      if (!configuration.itemId || !tenantId)
        return showErrorToast({ errors: "Something went wrong" });

      const res = await mutateAsync({
        itemId: configuration.itemId,
        projectKey: tenantId,
        isEnabled: !configuration.isDisabled,
      });

      if (!res.isSuccess) return showErrorToast({ errors: res.errors });

      showSuccessToast({
        description: `SSO provider has been successfully ${configuration.isDisabled ? "enabled" : "disabled"}`,
      });
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });

      showErrorToast({ errors: "Something went wrong" });
    }
  };

  const isCurrentlyDisabled = configuration.isDisabled;
  const dialogTitle = isCurrentlyDisabled ? "Enable" : "Disable";
  const dialogSubtitle = isCurrentlyDisabled
    ? "Are you sure you want to enable this provider? It will be available for user sign-in."
    : "Are you sure you want to disable this provider? Users will no longer be able to sign in using it.";

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <ConfirmationModal
        data={{
          dialogTitle,
          dialogSubtitle,
        }}
        onCancel={() => setOpen(false)}
        onConfirm={() => udpateStatus()}
      />
    </Dialog>
  );
};
