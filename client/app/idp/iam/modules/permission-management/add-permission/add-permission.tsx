
import { useProjectStore } from "@/store/useProjectStore";
import { CreatePermissionPayload } from "@blocks-idp/iam/models/permission";
import { useAddPermission } from "@blocks-idp/iam/hooks/use-permission";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { PermissionForm } from "../permission-form";
import { permissionFormSchemaType } from "../permission-form/utils";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useNavigate } from "react-router-dom";

export const AddPermission = () => {
  const navigate = useNavigate();
  const selectedTenantId = useProjectStore().selectedProject?.tenantId || "";

  const { isPending, mutateAsync } = useAddPermission();

  const onSubmit = async (data: permissionFormSchemaType) => {
    try {
      const newPermission: CreatePermissionPayload = {
        ...data,
        type: +data.type,
        projectKey: selectedTenantId,
        isBuiltIn: false,
        dependentPermissions: +data.type === 2 ? data.dependentPermissions : [],
      };
      const res = await mutateAsync(newPermission);
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({ description: "Permission created successfully" });
      navigate(`/services/iam?tab=permissions`);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  };

  BREADCRUMB_CUSTOM_TITLES["/services/iam/permission-detail"] = "Permissions";
  BREADCRUMB_CUSTOM_TITLES[`/services/iam/permission-detail/new`] = "New";

  return (
    <div className="px-4 pt-4 md:px-6 md:pt-6">
      <div className="hidden md:flex">
        <PageBreadcrumb breadcrumbIndex={3} />
      </div>
      <div className="mt-4 text-xl font-semibold">New Permission</div>
      <div className="mt-4">
        <PermissionForm onSave={onSubmit} isPending={isPending} />
      </div>
    </div>
  );
};
