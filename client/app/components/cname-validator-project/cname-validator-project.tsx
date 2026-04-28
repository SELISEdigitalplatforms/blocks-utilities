import { LoaderCircle } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useGetProject, useValidateCNameProject } from "@/hooks/use-project";
import { useProjectStore } from "@/store/useProjectStore";

export const CnameValidatorProject = () => {
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  const { itemId } = useProjectStore().selectedProject || { itemId: "", tenantId: "" };
  const { data } = useGetProject({ projectId: itemId });
  const { mutateAsync, isPending } = useValidateCNameProject({ projectKey });

  const cNameValidator = async () => {
    try {
      if (!data?.data.applicationDomain) return;
      const res = await mutateAsync({
        projectKey,
        cookieDomain: data?.data.applicationDomain.split("//")[1],
      });
      if (res.isSuccess)
        return showSuccessToast({ description: "CName is validated successfully" });
      showErrorToast({
        errors: "Could not verify the domain. Please make sure it is valid and try again",
      });
    } catch (error) {
      if (error && typeof error === "object" && "errors" in error) {
        showErrorToast({ errors: (error as unknown as { errors: unknown }).errors });
      }
    }
  };
  return (
    <Button onClick={cNameValidator} disabled={isPending} size="sm">
      {isPending && <LoaderCircle className="mr-2 h-4 w-4 animate-spin" />}
      CNAME Lookup
    </Button>
  );
};
