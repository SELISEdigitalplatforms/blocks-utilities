
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui-kits/sheet/sheet";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useGetPermissions } from "@blocks-idp/iam/hooks/use-permission";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { useGetUserById, useUpdateUser } from "@blocks-idp/iam/hooks/use-user";
import { IMembership } from "@blocks-idp/iam/models/user";
import { useCallback, useEffect, useState } from "react";
import { RemoveMembership } from "../remove-membership";
import { MembershipPermissionsTab } from "./membership-permission-tab";
import { MembershipRolesTab } from "./membership-role-tab";
import { MembershipFooter } from "./membership-footer";

type EditMembershipProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  membership: IMembership;
  organizationName: string;
  userId: string;
  projectKey: string;
};

export const EditMembership = ({
  open,
  onOpenChange,
  membership,
  organizationName,
  userId,
  projectKey,
}: EditMembershipProps) => {
  const [isEditing, setIsEditing] = useState(false);
  const [activeTab, setActiveTab] = useState("roles");
  const [removeModalOpen, setRemoveModalOpen] = useState(false);

  // Roles state
  const [rolesSearch, setRolesSearch] = useState("");
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);

  // Permissions state
  const [permissionsSearch, setPermissionsSearch] = useState("");
  const [permissionsTypeFilter, setPermissionsTypeFilter] = useState<string>("all");
  const [permissionsPage, setPermissionsPage] = useState(0);
  const [selectedPermissions, setSelectedPermissions] = useState<string[]>([]);

  const { data: userData } = useGetUserById({ id: userId, projectKey });
  const { data: rolesData, isLoading: isRolesLoading } = useGetRoles({
    projectKey,
    page: 0,
    pageSize: 1000,
    sort: { property: "Name", isDescending: false },
    filter: { search: "" },
  });
  const { data: permissionsData, isLoading: isPermissionsLoading } = useGetPermissions({
    projectKey,
    page: permissionsPage,
    pageSize: 10,
    search: permissionsSearch,
    isBuiltIn: "",
    roles: [],
    type: permissionsTypeFilter !== "all" ? parseInt(permissionsTypeFilter) : null,
  });

  const { mutateAsync, isPending } = useUpdateUser({ id: userId, projectKey });

  const allRoles = rolesData?.data || [];
  const allPermissions = permissionsData?.data || [];
  const totalPermissions = permissionsData?.totalCount || 0;
  const totalPermissionPages = Math.ceil(totalPermissions / 10);

  const resetSelections = useCallback(() => {
    setSelectedRoles(membership.roles || []);
    setSelectedPermissions(membership.permissions || []);
  }, [membership]);

  const resetFilters = useCallback(() => {
    setRolesSearch("");
    setPermissionsSearch("");
    setPermissionsTypeFilter("all");
    setPermissionsPage(0);
  }, []);

  useEffect(() => {
    if (open) {
      resetSelections();
      return;
    }

    setIsEditing(false);
    setActiveTab("roles");
    resetFilters();
    resetSelections();
  }, [open, resetFilters, resetSelections]);

  // Filter all roles by search in edit mode
  const filteredRolesForEdit = allRoles.filter((role) =>
    role.name.toLowerCase().includes(rolesSearch.toLowerCase()),
  );

  const handleRolesSearchChange = useCallback((value: string) => {
    setRolesSearch(value);
  }, []);

  const handleRoleToggle = useCallback((roleSlug: string) => {
    setSelectedRoles((prev) =>
      prev.includes(roleSlug) ? prev.filter((r) => r !== roleSlug) : [...prev, roleSlug],
    );
  }, []);

  const handlePermissionsSearchChange = useCallback((value: string) => {
    setPermissionsSearch(value);
    setPermissionsPage(0);
  }, []);

  const handlePermissionsTypeFilterChange = useCallback((value: string) => {
    setPermissionsTypeFilter(value);
    setPermissionsPage(0);
  }, []);

  const handlePermissionsPageChange = useCallback((page: number) => {
    setPermissionsPage(page);
  }, []);

  const handlePermissionToggle = useCallback((permissionName: string) => {
    setSelectedPermissions((prev) =>
      prev.includes(permissionName)
        ? prev.filter((p) => p !== permissionName)
        : [...prev, permissionName],
    );
  }, []);

  const handleSave = async () => {
    try {
      const existingMemberships = userData?.data?.memberships || [];
      const updatedMemberships = existingMemberships.map((m) =>
        m.organizationId === membership.organizationId
          ? { ...m, roles: selectedRoles, permissions: selectedPermissions }
          : m,
      );

      const res = await mutateAsync({
        ...userData?.data,
        memberships: updatedMemberships,
        itemId: userId,
        projectKey,
      });

      if (!res.isSuccess) {
        showErrorToast({ errors: res.errors });
        return;
      }

      showSuccessToast({ description: "Membership updated successfully" });
      setIsEditing(false);
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      } else {
        showErrorToast({ errors: "Something went wrong" });
      }
    }
  };

  const handleCancel = useCallback(() => {
    resetSelections();
    setRolesSearch("");
    setPermissionsSearch("");
    setIsEditing(false);
  }, [resetSelections]);

  const handleEdit = useCallback(() => setIsEditing(true), []);

  const handleUnassignOpen = useCallback(() => setRemoveModalOpen(true), []);

  const handleUnassignSuccess = useCallback(() => {
    setRemoveModalOpen(false);
    onOpenChange(false);
  }, [onOpenChange]);

  const getRoleDisplayName = useCallback(
    (roleSlug: string) => {
      const role = allRoles.find((r) => r.slug === roleSlug);
      return role?.name || roleSlug;
    },
    [allRoles],
  );

  return (
    <>
      <Sheet open={open} onOpenChange={onOpenChange}>
        <SheetContent side="right" className="flex w-full flex-col sm:max-w-lg">
          <SheetHeader>
            <SheetTitle>{organizationName}</SheetTitle>
          </SheetHeader>

          <Tabs
            value={activeTab}
            onValueChange={setActiveTab}
            className="mt-4 flex min-h-0 flex-1 flex-col overflow-y-auto"
          >
            <TabsList className="w-fit">
              <TabsTrigger value="roles">Roles</TabsTrigger>
              <TabsTrigger value="permissions">Permissions</TabsTrigger>
            </TabsList>

            <MembershipRolesTab
              isEditing={isEditing}
              rolesSearch={rolesSearch}
              onRolesSearchChange={handleRolesSearchChange}
              isRolesLoading={isRolesLoading}
              filteredRoles={filteredRolesForEdit}
              selectedRoles={selectedRoles}
              onRoleToggle={handleRoleToggle}
              getRoleDisplayName={getRoleDisplayName}
            />

            <MembershipPermissionsTab
              isEditing={isEditing}
              permissionsSearch={permissionsSearch}
              onPermissionsSearchChange={handlePermissionsSearchChange}
              permissionsTypeFilter={permissionsTypeFilter}
              onPermissionsTypeFilterChange={handlePermissionsTypeFilterChange}
              permissionsPage={permissionsPage}
              onPermissionsPageChange={handlePermissionsPageChange}
              isPermissionsLoading={isPermissionsLoading}
              allPermissions={allPermissions}
              selectedPermissions={selectedPermissions}
              totalPermissions={totalPermissions}
              totalPermissionPages={totalPermissionPages}
              onPermissionToggle={handlePermissionToggle}
            />
          </Tabs>

          <MembershipFooter
            isEditing={isEditing}
            isPending={isPending}
            onCancel={handleCancel}
            onSave={handleSave}
            onEdit={handleEdit}
            onUnassign={handleUnassignOpen}
          />
        </SheetContent>
      </Sheet>

      <RemoveMembership
        open={removeModalOpen}
        onOpenChange={setRemoveModalOpen}
        membership={membership}
        organizationName={organizationName}
        userId={userId}
        projectKey={projectKey}
        onSuccess={handleUnassignSuccess}
      />
    </>
  );
};
