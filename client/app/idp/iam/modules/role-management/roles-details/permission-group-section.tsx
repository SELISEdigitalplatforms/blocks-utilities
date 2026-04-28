import { AccordionContent, AccordionItem } from "@/components/ui-kits/accordion/accordion";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { BadgeAlert } from "lucide-react";
import { useMemo } from "react";
import { PermissionSelectionRow } from "./permission-selection-row";
import { isChecked } from "./permission-selection-utils";
import { PermissionGroup, useRoleDetailsStore } from "./role-details-state";

type PermissionGroupSectionProps = {
  group: PermissionGroup;
  onTrigger: () => void;
};

export const PermissionGroupSection = ({ group, onTrigger }: PermissionGroupSectionProps) => {
  const changePermissionGroupSelection = useRoleDetailsStore(
    (state) => state.changePermissionGroupSelection,
  );
  const permissionMap = useRoleDetailsStore((state) => state.permissionMap);
  const isEditMode = useRoleDetailsStore((state) => state.isEditMode);

  const checkedPermissions = useMemo(() => {
    return group.permissions.filter((perm) => isChecked(perm.resource, permissionMap));
  }, [group.permissions, permissionMap]);

  const isAllDependencyPermissionsChecked = useMemo(() => {
    return checkedPermissions.every((perm) => {
      if (!perm.dependentPermissions || perm.dependentPermissions.length === 0) return true;
      return perm.dependentPermissions.every((dp) => isChecked(dp, permissionMap));
    });
  }, [checkedPermissions, permissionMap]);

  const checked =
    checkedPermissions.length === group.permissions.length
      ? true
      : checkedPermissions.length === 0
        ? false
        : "indeterminate";

  return (
    <AccordionItem
      value={group.name}
      className="mb-4 overflow-hidden rounded-sm border bg-background"
    >
      <div
        className="bg-primary/5 px-4 py-3 hover:no-underline focus:no-underline dark:bg-gray-900 [&>svg]:hidden"
        onClick={(e) => {
          e.stopPropagation();
          onTrigger();
        }}
      >
        <div className="flex w-full items-start justify-between gap-3">
          <div className="flex items-start gap-2">
            <Checkbox
              id={`group-${group.name}`}
              checked={checked}
              onCheckedChange={(checked) =>
                changePermissionGroupSelection(group.permissions, !!checked)
              }
              onClick={(e) => e.stopPropagation()}
              className="mt-0.5"
              disabled={!isEditMode}
            />
            <div className="text-left">
              <div className="flex items-center gap-1">
                <h3 className="text-sm font-bold uppercase text-foreground">{group.name}</h3>
                <TooltipProvider>
                <Tooltip>
                  {!isAllDependencyPermissionsChecked && (
                    <TooltipTrigger>
                      <BadgeAlert className="ml-1 h-3.5 w-3.5 text-yellow-600" />
                    </TooltipTrigger>
                  )}
                  <TooltipContent side="right">
                    One or more permissions have missing dependencies
                  </TooltipContent>
                </Tooltip>
                </TooltipProvider>
              </div>
              <span className="text-xs text-foreground/60">
                {group.permissions.length} total permissions
              </span>
            </div>
          </div>

          <span className="rounded-full bg-primary/20 px-2.5 py-1 text-[10px] font-medium uppercase text-foreground">
            {checkedPermissions.length} selected
          </span>
        </div>
      </div>
      <AccordionContent className="bg-background p-3">
        <ul className="space-y-2">
          {group.permissions.map((permission) => (
            <PermissionSelectionRow key={permission.itemId} permission={permission} />
          ))}
        </ul>
      </AccordionContent>
    </AccordionItem>
  );
};
