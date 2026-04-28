
import { useGetPermissionsGroupBySeverity } from "@blocks-idp/iam/hooks/use-permission";
import { useProjectStore } from "@/store/useProjectStore";

import { PermissionSeverity } from "@blocks-idp/iam/components/permission-severity/permission-severity";

export const PermissionsGroupBySeverity = () => {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { data, isLoading } = useGetPermissionsGroupBySeverity({ projectKey: tenantId });
  return <PermissionSeverity data={data || []} isLoading={isLoading} />;
};
