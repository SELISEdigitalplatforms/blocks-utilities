// permission-selection.tsx
import { useEffect, useState, forwardRef, useImperativeHandle } from "react";
import { useQuery } from "@tanstack/react-query";
import { permissionService } from "@blocks-idp/iam/services/permission.service";
import { useProjectStore } from "@/store/useProjectStore";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui-kits/accordion/accordion";
import {
  IGetResourceGroupResponse,
  IPermission,
  ResourceType,
} from "@blocks-idp/iam/models/permission";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { useGetPermissions } from "@blocks-idp/iam/hooks/use-permission";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";

interface PermissionSelectionProps {
  // removed isBuiltIn prop
  slug: string;
  resourceGroups: IGetResourceGroupResponse;
}

export const PermissionSelection = forwardRef(function PermissionSelection(
  { slug, resourceGroups }: PermissionSelectionProps,
  ref,
) {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const {
    isLoading: isPermissionLoading,
    isFetching: isPermissionFetching,
    data: permissions,
  } = useQuery({
    queryKey: ["permissions", slug],
    queryFn: () =>
      permissionService.getPermissions({
        page: 0,
        pageSize: 10000,
        roles: [slug as string],
        projectKey: tenantId,
        filter: {
          search: "",
          isBuiltIn: "", // always empty -> include both built-in & custom
        },
      }),
    enabled: !!slug,
  });

  // Track selected permissions (itemId)
  const [selectedPermissions, setSelectedPermissions] = useState<string[]>([]);

  // Track initial permissions for comparison when saving
  const [initialPermissions, setInitialPermissions] = useState<string[]>([]);

  const [openGroup, setOpenGroup] = useState<string | null>(null);

  const [groupedPermissionListCache, setGroupedPermissionListCache] = useState<{
    [key: string]: IPermission[];
  }>({});

  // Fetch permissions for the open group using the hook at the top level
  const { data: groupedPermissionList, isLoading: isGroupedPermissionLoading } = useGetPermissions({
    page: 0,
    pageSize: 5000, // Increase to ensure we get all permissions for a group
    isBuiltIn: "", // always empty
    roles: [],
    search: "",
    resourceGroup: openGroup ? openGroup : "",
    projectKey: tenantId,
  });

  // Expose handleSave to parent via ref
  const handleSave = () => {
    // Return all selected permissions from all groups
    const allSelectedPermissions: IPermission[] = [];

    // Gather all permissions from cache that are selected
    Object.values(groupedPermissionListCache).forEach((groupPermissions) => {
      const selected = groupPermissions.filter((perm) => selectedPermissions.includes(perm.itemId));
      allSelectedPermissions.push(...selected);
    });

    // Add selected permissions from currently fetched role permissions that might not be in cache
    if (permissions) {
      const selectedFromRoles = permissions.data.filter(
        (perm) =>
          selectedPermissions.includes(perm.itemId) &&
          !allSelectedPermissions.some((p) => p.itemId === perm.itemId),
      );
      allSelectedPermissions.push(...selectedFromRoles);
    }

    // Calculate added and removed permissions
    const allPermissionsInCache: IPermission[] = [];
    Object.values(groupedPermissionListCache).forEach((perms) => {
      allPermissionsInCache.push(...perms);
    });

    // Find permissions that were added (in selectedPermissions but not in initialPermissions)
    const addedPermissions = allSelectedPermissions.filter(
      (perm) => !initialPermissions.includes(perm.itemId),
    );

    // Find permissions that were removed (in initialPermissions but not in selectedPermissions)
    const removedPermissionIds = initialPermissions.filter(
      (id) => !selectedPermissions.includes(id),
    );

    // Find the full permission objects for removed permissions
    const removedPermissions: IPermission[] = [];
    if (permissions) {
      permissions.data.forEach((perm) => {
        if (removedPermissionIds.includes(perm.itemId)) {
          removedPermissions.push(perm);
        }
      });
    }

    // Also check cache for removed permissions
    allPermissionsInCache.forEach((perm) => {
      if (
        removedPermissionIds.includes(perm.itemId) &&
        !removedPermissions.some((p) => p.itemId === perm.itemId)
      ) {
        removedPermissions.push(perm);
      }
    });

    return {
      addedPermissions,
      removedPermissions,
    };
  };

  useImperativeHandle(ref, () => ({
    handleSave,
  }));

  // Initialize selectedPermissions from fetched permissions
  useEffect(() => {
    if (permissions && permissions.data) {
      const permissionIds = permissions.data.map((p) => p.itemId);
      setSelectedPermissions(permissionIds);
      setInitialPermissions(permissionIds); // Store initial state for later comparison
    }
  }, [permissions]);

  // Update cache when new group data is fetched
  useEffect(() => {
    if (groupedPermissionList && groupedPermissionList.data && openGroup) {
      setGroupedPermissionListCache((prev) => ({
        ...prev,
        [openGroup]: groupedPermissionList.data,
      }));
    }
  }, [groupedPermissionList, openGroup]);

  if (isPermissionLoading || isPermissionFetching) {
    return (
      <div className="mt-4">
        {[1, 2, 3].map((i) => (
          <div key={i} className="mb-4 rounded-sm border bg-background px-4 py-4">
            <div className="mb-2 flex items-start gap-2">
              <Skeleton className="mt-1 h-4 w-4" />
              <div className="w-full">
                <Skeleton className="mb-2 h-6 w-1/3" />
                <Skeleton className="h-4 w-1/4" />
              </div>
            </div>
            <div className="ml-6 mt-4">
              {[1, 2, 3].map((j) => (
                <div key={j} className="mb-3 flex items-center gap-2">
                  <Skeleton className="h-4 w-4" />
                  <Skeleton className="h-5 w-2/3" />
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
    );
  }

  // Function to get permissions for a specific group from cache
  const getGroupPermissions = (groupName: string) => {
    return groupedPermissionListCache[groupName] || [];
  };

  // For each group, build FE Action hierarchy for the current group's permissions
  const buildGroupPermissionData = (groupName: string) => {
    const perms = getGroupPermissions(groupName);

    // Skip processing if no permissions are loaded for this group
    if (perms.length === 0) {
      return {
        group: groupName,
        feActions: [],
        independentPerms: [],
        allIds: [],
      };
    }

    // Create permission and resource maps
    const permissionMap: Record<string, IPermission> = {};
    const resourceMap: Record<string, IPermission> = {};

    perms.forEach((perm) => {
      permissionMap[perm.itemId] = perm;
      resourceMap[perm.resource] = perm;
    });

    // Find FE Actions
    const feActions = perms.filter((p) => p.type === 2);
    const feActionsWithDependents = feActions.map((fa) => ({
      ...fa,
      dependents: fa.dependentPermissions
        .map((depResource) => resourceMap[depResource])
        .filter(Boolean) as IPermission[],
    }));

    // Gather all dependent itemIds
    const allDepIdsInGroup = feActionsWithDependents.flatMap((fa) =>
      fa.dependents.map((d) => d.itemId),
    );

    // Exclude those dependent itemIds from independents
    const independentPerms = perms.filter(
      (p) => p.type !== 2 && !allDepIdsInGroup.includes(p.itemId),
    );

    // Combine IDs
    const allIds = [
      ...feActionsWithDependents.map((fa) => fa.itemId),
      ...allDepIdsInGroup,
      ...independentPerms.map((p) => p.itemId),
    ];
    const uniqueIds = Array.from(new Set(allIds));

    return {
      group: groupName,
      feActions: feActionsWithDependents,
      independentPerms,
      allIds: uniqueIds,
    };
  };

  // Handle permission checkbox toggle (with FE Action logic)
  const handleToggle = (perm: IPermission, parent?: IPermission) => {
    setSelectedPermissions((prev) => {
      let next = [...prev];

      // Get all permissions for current group to build resource map
      const groupPerms = getGroupPermissions(perm.resourceGroup);
      const resourceMap: Record<string, IPermission> = {};
      groupPerms.forEach((p) => {
        resourceMap[p.resource] = p;
      });

      if (perm.type === 2 && perm.dependentPermissions?.length) {
        // FE Action: toggle itself and all dependents
        const allDependents = perm.dependentPermissions
          .map((depRes) => resourceMap[depRes]?.itemId)
          .filter(Boolean);

        const allIds = [perm.itemId, ...allDependents];
        const isAllSelected = allIds.every((id) => prev.includes(id));

        if (isAllSelected) {
          // Unselect FE Action and all dependents
          next = next.filter((id) => !allIds.includes(id));
        } else {
          // Select FE Action and all dependents
          allIds.forEach((id) => {
            if (!next.includes(id)) next.push(id);
          });
        }
      } else if (parent && parent.type === 2) {
        // Dependent: toggle itself, update parent if needed
        const allDependents = parent.dependentPermissions
          .map((depRes) => resourceMap[depRes]?.itemId)
          .filter(Boolean);

        if (prev.includes(perm.itemId)) {
          // Unselect dependent
          next = next.filter((id) => id !== perm.itemId);
          // If any dependent is unchecked, parent should be unchecked
          if (allDependents.some((depId) => depId === perm.itemId)) {
            next = next.filter((id) => id !== parent.itemId);
          }
        } else {
          // Select dependent
          next.push(perm.itemId);
          // If all dependents are checked, check parent
          if (allDependents.every((depId) => depId === perm.itemId || next.includes(depId))) {
            if (!next.includes(parent.itemId)) next.push(parent.itemId);
          }
        }
      } else {
        // Regular permission
        if (prev.includes(perm.itemId)) {
          next = next.filter((id) => id !== perm.itemId);
        } else {
          next.push(perm.itemId);
        }
      }
      return next;
    });
  };

  // Handle group checkbox toggle
  const handleGroupToggle = (groupObj: ReturnType<typeof buildGroupPermissionData>) => {
    setSelectedPermissions((prev) => {
      const isAllSelected = groupObj.allIds.every((id) => prev.includes(id));
      if (isAllSelected) {
        return prev.filter((id) => !groupObj.allIds.includes(id));
      } else {
        const next = [...prev];
        groupObj.allIds.forEach((id) => {
          if (!next.includes(id)) next.push(id);
        });
        return next;
      }
    });
  };

  // Check if all permissions in group are selected
  const isGroupAllSelected = (groupObj: ReturnType<typeof buildGroupPermissionData>) => {
    return (
      groupObj.allIds.length > 0 && groupObj.allIds.every((id) => selectedPermissions.includes(id))
    );
  };

  // Get selected count for a group - use cache if available, otherwise use permissions
  const getSelectedCount = (groupName: string) => {
    // If we have cached data for this group, use it to calculate selected count
    if (groupedPermissionListCache[groupName]?.length > 0) {
      return groupedPermissionListCache[groupName].filter((perm) =>
        selectedPermissions.includes(perm.itemId),
      ).length;
    }

    // Otherwise fall back to permissions data
    if (!permissions || !permissions.data) return 0;
    return permissions.data.filter((perm) => perm.resourceGroup === groupName).length;
  };

  // Function to call when a group is opened
  const getGroupData = (group: string) => {
    setOpenGroup(group);
  };

  return (
    <div>
      <div className="mt-4">
        <Accordion
          type="single"
          collapsible
          onValueChange={(value) => {
            if (value) getGroupData(value);
            else setOpenGroup(null);
          }}
        >
          {resourceGroups.map((groupResource) => {
            const groupName = groupResource.resourceGroup;
            const groupObj = buildGroupPermissionData(groupName);
            const isLoading = openGroup === groupName && isGroupedPermissionLoading;

            return (
              <AccordionItem
                key={groupName}
                value={groupName}
                className="mb-4 rounded-sm border bg-background px-4"
              >
                <AccordionTrigger className="mb-2 flex items-start justify-between gap-1 hover:no-underline focus:no-underline">
                  <div className="flex flex-col items-start">
                    <div className="flex items-center gap-2">
                      <Checkbox
                        checked={isGroupAllSelected(groupObj)}
                        onCheckedChange={() => handleGroupToggle(groupObj)}
                        id={`group-${groupName}`}
                      />
                      <h3 className="text-left text-2xl font-semibold">{groupName}</h3>
                    </div>
                    <span className="text-left text-muted-foreground">
                      Total Permissions: {groupResource.count} | Selected:{" "}
                      {getSelectedCount(groupName)}
                    </span>
                  </div>
                </AccordionTrigger>
                <AccordionContent>
                  {isLoading ? (
                    <div className="py-4 text-center">Loading group permissions...</div>
                  ) : groupObj.allIds.length === 0 ? (
                    <div className="py-4 text-center">Click to load permissions for this group</div>
                  ) : (
                    <ul className="ml-4">
                      {/* FE Actions with dependents */}
                      {groupObj.feActions.map((fa) => (
                        <li key={fa.itemId} className="mb-2">
                          <div className="flex items-center gap-2">
                            <Checkbox
                              checked={selectedPermissions.includes(fa.itemId)}
                              onCheckedChange={() => handleToggle(fa)}
                              id={`perm-${fa.itemId}`}
                            />
                            <label
                              htmlFor={`perm-${fa.itemId}`}
                              className="cursor-pointer font-medium"
                            >
                              {fa.name}
                            </label>
                            <span className="ml-2 rounded border bg-muted px-2 py-0.5 text-xs">
                              FE Action
                            </span>
                          </div>
                          {/* Render dependents */}
                          {fa.dependents.length > 0 && (
                            <ul className="ml-8 mt-1">
                              {fa.dependents.map((dep) => (
                                <li key={dep.itemId} className="mb-1 flex items-center gap-2">
                                  <Checkbox
                                    checked={selectedPermissions.includes(dep.itemId)}
                                    onCheckedChange={() => handleToggle(dep, fa)}
                                    id={`perm-${dep.itemId}`}
                                  />
                                  <label htmlFor={`perm-${dep.itemId}`} className="cursor-pointer">
                                    {dep.name}
                                  </label>
                                  {dep.type && (
                                    <span className="ml-2 rounded border bg-muted px-2 py-0.5 text-xs">
                                      {ResourceType[dep.type]}
                                    </span>
                                  )}
                                </li>
                              ))}
                            </ul>
                          )}
                        </li>
                      ))}
                      {/* Independent permissions */}
                      {groupObj.independentPerms.map((perm) => (
                        <li key={perm.itemId} className="mb-2 flex items-center gap-2">
                          <Checkbox
                            checked={selectedPermissions.includes(perm.itemId)}
                            onCheckedChange={() => handleToggle(perm)}
                            id={`perm-${perm.itemId}`}
                          />
                          <label
                            htmlFor={`perm-${perm.itemId}`}
                            className="cursor-pointer font-medium"
                          >
                            {perm.name}
                          </label>
                          {perm.type && (
                            <span className="ml-2 rounded border bg-muted px-2 py-0.5 text-xs">
                              {ResourceType[perm.type]}
                            </span>
                          )}
                        </li>
                      ))}
                    </ul>
                  )}
                </AccordionContent>
              </AccordionItem>
            );
          })}
        </Accordion>
      </div>
    </div>
  );
});
