import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { cn } from "@/lib/utils";
import { PERMISSION_SEVERITY_OPTIONS, ResourceType } from "@blocks-idp/iam/models/permission";
import { BadgeAlert, BadgeCheck } from "lucide-react";
import { useMemo } from "react";
import { PermissionState, useRoleDetailsStore } from "./role-details-state";

type PermissionToggleCardProps = {
  permission: PermissionState;
  checked: boolean;
  onCheckedChange?(checked: import("@radix-ui/react-checkbox").CheckedState): void;
  id: string;
  hasDependentPermissions?: boolean;
  isAllDependentPermissionsChecked?: boolean;
};

export const PermissionToggleCard = ({
  permission,
  checked,
  onCheckedChange,
  id,
  hasDependentPermissions,
  isAllDependentPermissionsChecked,
}: PermissionToggleCardProps) => {
  const isEditMode = useRoleDetailsStore((state) => state.isEditMode);
  const permissionSeverity = useMemo(() => {
    return PERMISSION_SEVERITY_OPTIONS.find((option) => option.value === permission.permissionSeverity);
  }, [permission.permissionSeverity]);

  return (
    <div
      className={cn(
        "flex items-center justify-between gap-3 rounded-md border p-3 cursor-pointer [&_*]:cursor-[inherit]",
        !isEditMode && "cursor-not-allowed"
      )}
    >
      <Checkbox
        checked={checked}
        onCheckedChange={onCheckedChange}
        id={id}
        disabled={!isEditMode}
        className=" flex-shrink-0"
      />
      <label
        htmlFor={id}
        className={cn("w-full h-full font-medium text-foreground flex flex-1 flex-col md:flex-row gap-2")}
      >
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1">
            <h5>{permission.name}</h5>
            {hasDependentPermissions && (
              <TooltipProvider>
            <Tooltip>
                <TooltipTrigger>
                  {isAllDependentPermissionsChecked ? (
                    <BadgeCheck className="text-green-600 h-3.5 w-3.5" />
                  ) : (
                    <BadgeAlert className="text-yellow-600 h-3.5 w-3.5" />
                  )}
                </TooltipTrigger>
                <TooltipContent side="right">
                  {isAllDependentPermissionsChecked
                    ? "All dependent permissions are selected"
                    : "One or more dependent permissions are missing"}
                </TooltipContent>
              </Tooltip>
            </TooltipProvider>
            )}
          </div>
          <p className="mt-0.5 text-xs text-muted-foreground">
            {permission.description || "No description available."}
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
  );
};
