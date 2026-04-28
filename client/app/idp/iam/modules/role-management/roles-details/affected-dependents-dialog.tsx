import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { ScrollArea } from "@/components/ui-kits/scroll-area/scroll-area";
import { cn } from "@/lib/utils";
import { PERMISSION_SEVERITY_OPTIONS, ResourceType } from "@blocks-idp/iam/models/permission";
import { useMemo } from "react";
import { PermissionDialogProps } from "./permission-selection-utils";
import { useRoleDetailsStore } from "./role-details-state";

export const AffectedPermissionsDialog = ({ permission, onOpenChange, open }: PermissionDialogProps) => {
  const permissionMap = useRoleDetailsStore((state) => state.permissionMap);
  const changePermissionSelection = useRoleDetailsStore((state) => state.changePermissionSelection);
  const title = `Review Permission Changes`;
  const description = `The following permissions depend on this permission. Changing it may impact their functionality. Please review carefully`;

  const permissionSeverity = useMemo(() => {
    return PERMISSION_SEVERITY_OPTIONS.find((option) => option.value === permission.permissionSeverity);
  }, [permission.permissionSeverity]);

  const onSaveClick = () => {
    changePermissionSelection([{ permissionResource: permission.resource, isChecked: false }]);
    onOpenChange(false);
  };

  const parentPermissions = useMemo(
    () => permission.parents.map((parent) => permissionMap.get(parent)) || [],
    [permission.parents, permissionMap]
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent aria-describedby={undefined}>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        <ScrollArea className="max-h-400">
          <ul className="space-y-2">
            {parentPermissions.map((parent) => (
              <div
                key={`parent-${parent?.itemId}`}
                className={cn(
                  "flex items-center justify-between gap-3 rounded-md border p-3 cursor-pointer [&_*]:cursor-[inherit]"
                )}
              >
                <label
                  className={cn("w-full h-full font-medium text-foreground flex flex-1 flex-col md:flex-row gap-2")}
                >
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-1">
                      <h5>{parent?.name}</h5>
                    </div>
                    <p className="mt-0.5 text-xs text-muted-foreground">
                      {parent?.description || "No description available."}
                    </p>
                  </div>
                  <div className="md:self-center space-x-2">
                    <span
                      className={cn(
                        "rounded border px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide",
                        permissionSeverity?.className,
                        permissionSeverity?.bg
                      )}
                    >
                      {permissionSeverity?.label}
                    </span>
                    <span className="rounded border border-blue-200 bg-blue-50 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-blue-700">
                      {ResourceType[permission.type]}
                    </span>
                  </div>
                </label>
              </div>
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
