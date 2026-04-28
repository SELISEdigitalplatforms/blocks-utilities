import { CheckedState } from "@radix-ui/react-checkbox";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { useMemo, useState } from "react";
import { RequiredPermissionsDialog } from "./required-permission-dialog";
import { AffectedPermissionsDialog } from "./affected-dependents-dialog";
import { PermissionToggleCard } from "./permission-toggle-card";
import { isChecked } from "./permission-selection-utils";
import { PermissionState, useRoleDetailsStore } from "./role-details-state";

export const PermissionSelectionRow = ({ permission }: { permission: PermissionState }) => {
  const [requiredPermissionsModalState, setRequiredPermissionsModalState] = useState<{
    open: boolean;
    permission: IPermission | null;
    checked: boolean;
  }>({
    open: false,
    permission: null,
    checked: false,
  });
  const [affectedPermissionsModalState, setAffectedPermissionsDialogModalState] = useState<{
    open: boolean;
    permission: IPermission | null;
    checked: boolean;
  }>({
    open: false,
    permission: null,
    checked: false,
  });
  const permissionMap = useRoleDetailsStore((state) => state.permissionMap);

  const checked = useMemo(() => isChecked(permission.resource, permissionMap), [permission.resource, permissionMap]);

  const changePermissionSelection = useRoleDetailsStore((state) => state.changePermissionSelection);

  const onCheckedChangeHandler = (nextChecked: CheckedState) => {
    if (permission.dependentPermissions && permission.dependentPermissions.length > 0)
      return setRequiredPermissionsModalState({ open: true, permission, checked: !!nextChecked });

    if (
      !nextChecked &&
      permission.parents &&
      permission.parents.length > 0 &&
      permission.parents.some((parent) => isChecked(parent, permissionMap))
    ) {
      return setAffectedPermissionsDialogModalState({ open: true, permission, checked: !!nextChecked });
    }

    changePermissionSelection([{ permissionResource: permission.resource, isChecked: !!nextChecked }]);
  };

  const hasDependentPermissions = useMemo(
    () => permission.dependentPermissions && permission.dependentPermissions.length > 0,
    [permission.dependentPermissions]
  );

  const isAllDependentPermissionsChecked = useMemo(() => {
    if (!hasDependentPermissions) return false;
    return permission.dependentPermissions.every((dp) => isChecked(dp, permissionMap));
  }, [permission.dependentPermissions, hasDependentPermissions, permissionMap]);

  return (
    <li key={permission.itemId} className="mb-2">
      <PermissionToggleCard
        id={`root-${permission.itemId}`}
        permission={permission}
        checked={checked}
        onCheckedChange={onCheckedChangeHandler}
        hasDependentPermissions={checked && hasDependentPermissions}
        isAllDependentPermissionsChecked={isAllDependentPermissionsChecked}
      />
      <RequiredPermissionsDialog
        permission={permission}
        checked={requiredPermissionsModalState.checked}
        open={requiredPermissionsModalState.open}
        onOpenChange={(open) => setRequiredPermissionsModalState({ ...requiredPermissionsModalState, open })}
      />
      <AffectedPermissionsDialog
        permission={permission}
        checked={affectedPermissionsModalState.checked}
        open={affectedPermissionsModalState.open}
        onOpenChange={(open) => setAffectedPermissionsDialogModalState({ ...affectedPermissionsModalState, open })}
      />
    </li>
  );
};
