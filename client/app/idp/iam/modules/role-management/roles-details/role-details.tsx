

import { useMemo } from "react";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { useProjectStore } from "@/store/useProjectStore";
import { useSetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { Button } from "@/components/ui-kits/button/button";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { IPermission, PermissionSeverityLevel } from "@blocks-idp/iam/models/permission";
import { RoleDetailsProvider, useRoleDetailsStore } from "./role-details-state";
import { PermissionSeverity } from "@blocks-idp/iam/components/permission-severity/permission-severity";
import { useQueryClient } from "@tanstack/react-query";
import { PermissionsSelectionPanel } from "./permissions-selection-panel";

export function RoleDetailsContainer() {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const queryClient = useQueryClient();

  const role = useRoleDetailsStore((state) => state.role);
  const isEditMode = useRoleDetailsStore((state) => state.isEditMode);
  const discardChanges = useRoleDetailsStore((state) => state.discardChanges);
  const changeEditMode = useRoleDetailsStore((state) => state.changeEditMode);
  const isInitialized = useRoleDetailsStore((state) => state.isInitialized);
  const permissionMap = useRoleDetailsStore((state) => state.permissionMap);
  const { isPending, mutateAsync } = useSetRoles(role?.slug);

  BREADCRUMB_CUSTOM_TITLES["/services/iam/role-detail"] = "Roles";
  BREADCRUMB_CUSTOM_TITLES["/services/iam/role-detail/" + role?.itemId] = role?.name || "";

  const onSaveClick = async () => {
    const changedPermissions = Array.from(permissionMap.values()).reduce(
      (acc, item) => {
        if (!item.modified) return acc;
        if (item.changeState === "added") {
          acc.added.push(item.itemId);
          return acc;
        }
        if (item.changeState === "removed") {
          acc.removed.push(item.itemId);
          return acc;
        }
        return acc;
      },
      { added: [] as string[], removed: [] as string[] },
    );
    if (!role?.slug || (!changedPermissions.added.length && !changedPermissions.removed.length))
      return null;
    try {
      await mutateAsync({
        addPermissions: changedPermissions.added,
        removePermissions: changedPermissions.removed,
        projectKey: tenantId,
        slug: role.slug,
      });
      showSuccessToast({ description: "Role permissions updated successfully" });
      queryClient.invalidateQueries({ queryKey: ["permissions"] });
    } catch (error) {
      if (error && typeof error === "object" && "errors" in error) {
        const { errors } = error;
        showErrorToast({ errors });
      }
    }
  };

  const permissionSeverityData = useMemo(() => {
    const permissions = Array.from(permissionMap.values());
    return Object.values(
      permissions
        .filter((item) => {
          if (item.modified && item.changeState === "added") return true;
          if (item.modified && item.changeState === "removed") return false;
          return item.isInitiallyAssigned;
        })
        .reduce(
          (acc, item: IPermission) => {
            if (!acc[item.permissionSeverity]) {
              acc[item.permissionSeverity] = {
                severityLevel: PermissionSeverityLevel[item.permissionSeverity],
                count: 0,
              };
            }
            acc[item.permissionSeverity].count += 1;
            return acc;
          },
          {} as Record<string, { severityLevel: string; count: number }>,
        ),
    );
  }, [permissionMap]);

  return (
    <div className="px-4 pt-4 md:px-6 md:pt-6">
      <div className="hidden md:flex">
        <PageBreadcrumb breadcrumbIndex={3} />
      </div>

      <div className="mt-4 grid gap-4">
        <div className="flex items-center justify-between rounded text-base">
          {!isInitialized ? (
            <Skeleton className="h-8 w-1/3 rounded-sm" />
          ) : (
            <h3 className="text-2xl font-bold tracking-tight">{role?.name}</h3>
          )}
          <div className="flex items-center gap-2">
            {!isEditMode && (
              <Button variant="outline" onClick={() => changeEditMode(true)}>
                <span>Edit Permissions</span>
              </Button>
            )}
            {isEditMode && (
              <>
                <Button variant="outline" disabled={isPending} onClick={() => discardChanges()}>
                  <span>Discard</span>
                </Button>
                <Button disabled={isPending || !isInitialized} onClick={onSaveClick}>
                  <span>Save Changes</span>
                </Button>
              </>
            )}
          </div>
        </div>
        <PermissionSeverity data={permissionSeverityData} isLoading={!isInitialized} />
        <PermissionsSelectionPanel />
      </div>
    </div>
  );
}

export function RoleDetails({ params }: { params: { id: string } }) {
  const { id } = params;
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  return (
    <RoleDetailsProvider id={id} projectKey={tenantId}>
      <RoleDetailsContainer />
    </RoleDetailsProvider>
  );
}
