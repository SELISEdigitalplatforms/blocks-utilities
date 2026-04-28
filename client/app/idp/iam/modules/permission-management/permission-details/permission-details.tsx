

import { useProjectStore } from "@/store/useProjectStore";
import { useGetPermissionById, useUpdatePermission } from "@blocks-idp/iam/hooks/use-permission";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { PermissionForm } from "../permission-form";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { permissionFormSchemaType } from "../permission-form/utils";
import { PermissionRolesList } from "./permission-roles-list";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Badge } from "@/components/ui-kits/badge/badge";
import { cn } from "@/lib/utils";

type PermissionDetailsProps = {
  id: string;
};

const FormLOadingSkeleton = () => (
  <Card>
    <CardContent>
      <div className="grid w-full grid-cols-1 md:grid-cols-2 gap-4">
        {Array.from({ length: 5 }).map((_, index) => (
          <div key={index}>
            <Skeleton className="h-5 w-32" />
            <Skeleton className="h-8 w-full mt-2" />
          </div>
        ))}
      </div>
    </CardContent>
  </Card>
);

export const PermissionDetails = ({ id }: PermissionDetailsProps) => {
  const selectedTenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data, isLoading } = useGetPermissionById({ id, projectKey: selectedTenantId });
  const { isPending, mutateAsync } = useUpdatePermission({ id, projectKey: selectedTenantId });

  const onSubmit = async (data: permissionFormSchemaType) => {
    try {
      const res = await mutateAsync({
        ...data,
        type: +data.type,
        projectKey: selectedTenantId,
        isBuiltIn: false,
        dependentPermissions: +data.type === 2 ? data.dependentPermissions : [],
        itemId: id,
      });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({ description: "Permission Updated successfully" });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  };

  BREADCRUMB_CUSTOM_TITLES["/services/iam/permission-detail"] = "Permissions";
  BREADCRUMB_CUSTOM_TITLES[`/services/iam/permission-detail/${id}`] = data?.data.name || "";

  return (
    <div className="px-4 pt-4 md:px-6 md:pt-6">
      <div className="hidden md:flex">
        <PageBreadcrumb breadcrumbIndex={3} />
      </div>
      <div className="mt-4 text-xl font-semibold flex items-center gap-2">
        {data?.data.name || ""}
        {data?.data && (
          <Badge
            className={cn(data?.data.isBuiltIn ? "!bg-gray-300 !text-gray-800" : "!bg-purple-100 !text-purple-700")}
          >
            {data?.data.isBuiltIn ? "Built In" : "Custom"}
          </Badge>
        )}
      </div>
      <div className="mt-4">
        {isLoading ? (
          <FormLOadingSkeleton />
        ) : (
          <PermissionForm onSave={onSubmit} isPending={isPending} values={data?.data || null} />
        )}
      </div>
      {/* temporary solutions */}
      <PermissionRolesList slugs={data?.data.roles || []} />
    </div>
  );
};
