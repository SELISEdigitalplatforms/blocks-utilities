import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { ScrollArea } from "@/components/ui-kits/scroll-area/scroll-area";
import { useCallback, useState } from "react";
import { PermissionToggleCard } from "./permission-toggle-card";
import { PermissionDialogProps, isChecked } from "./permission-selection-utils";
import { useRoleDetailsStore } from "./role-details-state";

type SelectionState = {
  [key: string]: {
    isChecked: boolean;
    permissionResource: string;
  };
};

export const RequiredPermissionsDialog = ({ permission, onOpenChange, open }: PermissionDialogProps) => {
  const [selectionState, setSelectionState] = useState<SelectionState>({});

  const permissionMap = useRoleDetailsStore((state) => state.permissionMap);
  const changePermissionSelection = useRoleDetailsStore((state) => state.changePermissionSelection);

  const title = `Review Permission Changes`;
  const dependentPermissions = permission.dependentPermissions.map((dp) => permissionMap.get(dp));

  const onCheckedChangeHandler = (permResource: string, checked: boolean) => {
    setSelectionState((prev) => {
      const newState = { ...prev };
      newState[permResource] = { isChecked: checked, permissionResource: permResource };
      return newState;
    });
  };

  const isDependentPermissionChecked = useCallback(
    (permResource: string) => {
      if (selectionState[permResource]) return selectionState[permResource].isChecked;
      return isChecked(permResource, permissionMap);
    },
    [permissionMap, selectionState]
  );

  const onSaveClick = () => {
    const changePermissions = Object.values(selectionState);
    changePermissionSelection(changePermissions);
    onOpenChange(false);
  };

  const resetSelectionState = () => {
    setTimeout(() => {
      setSelectionState({});
    }, 500);
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(nextOpen) => {
        if (!nextOpen) resetSelectionState();
        onOpenChange(nextOpen);
      }}
    >
      <DialogContent aria-describedby={undefined}>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
        </DialogHeader>
        <PermissionToggleCard
          key={`model-${permission.itemId}`}
          id={`modal-${permission.itemId}`}
          permission={permission}
          checked={isDependentPermissionChecked(permission.resource)}
          onCheckedChange={(checked) => onCheckedChangeHandler(permission.resource, !!checked)}
          hasDependentPermissions={isDependentPermissionChecked(permission.resource)}
          isAllDependentPermissionsChecked={permission.dependentPermissions.every((dp) =>
            isDependentPermissionChecked(dp)
          )}
        />
        <div className="mt-2">
          <h5 className=" font-semibold">Dependencies</h5>
          <p className="text-sm text-muted-foreground">
            The following permissions are required for this permission to work.
          </p>
        </div>
        <ScrollArea className="max-h-[400px]">
          <ul className="space-y-2">
            {dependentPermissions.map((perm) => (
              <PermissionToggleCard
                key={`dependent-${perm?.itemId}`}
                id={`dependent-${perm?.itemId}`}
                permission={perm!}
                checked={isDependentPermissionChecked(perm!.resource)}
                onCheckedChange={(checked) => onCheckedChangeHandler(perm!.resource, !!checked)}
              />
            ))}
          </ul>
        </ScrollArea>
        <DialogFooter>
          <DialogClose asChild>
            <Button variant="outline">Cancel</Button>
          </DialogClose>
          <Button onClick={onSaveClick}>Save</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
