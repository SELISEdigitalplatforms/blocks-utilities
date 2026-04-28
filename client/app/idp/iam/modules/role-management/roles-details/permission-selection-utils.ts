import { PermissionMap, PermissionState } from "./role-details-state";

export type PermissionDialogProps = {
  permission: PermissionState;
  checked: boolean;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

export const isChecked = (permissionResource: string, permissionMap: PermissionMap) => {
  const permission = permissionMap.get(permissionResource);
  if (!permission) return false;
  if (permission.modified && permission.changeState === "added") return true;
  if (permission.modified && permission.changeState === "removed") return false;
  return permission.isInitiallyAssigned;
};
