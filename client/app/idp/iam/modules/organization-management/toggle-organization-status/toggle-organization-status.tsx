
import { Button } from "@/components/ui-kits/button/button";
import {
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useProjectStore } from "@/store/useProjectStore";
import { IOrganization } from "@blocks-idp/iam/models/organization";
import { useSaveOrganization } from "@blocks-idp/iam/hooks/use-organization";

type ToggleOrganizationStatusProps = {
  organization: IOrganization;
  onClose: () => void;
};

export const ToggleOrganizationStatus = ({
  organization,
  onClose,
}: ToggleOrganizationStatusProps) => {
  const { mutateAsync, isPending } = useSaveOrganization();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  const isEnabling = !organization.isEnable;
  const action = isEnabling ? "enable" : "disable";
  const actionLabel = isEnabling ? "Enable" : "Disable";
  const actioningLabel = isEnabling ? "Enabling..." : "Disabling...";

  const handleConfirm = async () => {
    try {
      const res = await mutateAsync({
        projectKey: tenantId,
        name: organization.name,
        itemId: organization.itemId,
        isEnable: isEnabling,
      });
      if (!res.isSuccess) {
        showErrorToast({ errors: res.errors });
        return;
      }
      showSuccessToast({
        description: `Organization ${isEnabling ? "enabled" : "disabled"} successfully`,
      });
      onClose();
    } catch (error: unknown) {
      if (error && typeof error === "object" && "errors" in error) {
        showErrorToast({ errors: error.errors });
      }
    }
  };

  return (
    <DialogContent>
      <DialogHeader className="mb-4">
        <DialogTitle>{actionLabel} Organization</DialogTitle>
        <DialogDescription>
          Are you sure you want to {action} the organization &quot;{organization.name}&quot;?
          {!isEnabling && " This will make it inactive."}
          {isEnabling && " This will make it active again."}
        </DialogDescription>
      </DialogHeader>
      <DialogFooter className="mt-6">
        <DialogTrigger asChild>
          <Button className="min-w-[80px]" variant="outline" disabled={isPending}>
            Cancel
          </Button>
        </DialogTrigger>
        <Button
          className="min-w-[80px]"
          variant={isEnabling ? "default" : "destructive"}
          onClick={handleConfirm}
          disabled={isPending}
        >
          {isPending ? actioningLabel : actionLabel}
        </Button>
      </DialogFooter>
    </DialogContent>
  );
};
