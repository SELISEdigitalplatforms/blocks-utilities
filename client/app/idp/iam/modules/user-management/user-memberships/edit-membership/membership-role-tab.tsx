import { Input } from "@/components/ui-kits/input/input";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Checkbox } from "@radix-ui/react-checkbox";
import { TabsContent } from "@radix-ui/react-tabs";

type MembershipRolesTabProps = {
  isEditing: boolean;
  rolesSearch: string;
  onRolesSearchChange: (value: string) => void;
  isRolesLoading: boolean;
  filteredRoles: { slug: string; name: string }[];
  selectedRoles: string[];
  onRoleToggle: (roleSlug: string) => void;
  getRoleDisplayName: (roleSlug: string) => string;
};

export const MembershipRolesTab = ({
  isEditing,
  rolesSearch,
  onRolesSearchChange,
  isRolesLoading,
  filteredRoles,
  selectedRoles,
  onRoleToggle,
  getRoleDisplayName,
}: MembershipRolesTabProps) => (
  <TabsContent value="roles" className="mt-4">
    {isEditing ? (
      <div className="flex flex-col gap-4">
        <p className="text-sm text-muted-foreground">Select at least one role to assign</p>
        <Input
          data-testid="role-tab-searchbar"
          name="role-tab-searchbar"
          placeholder="Search"
          value={rolesSearch}
          onChange={(e) => onRolesSearchChange(e.target.value)}
          className="focus-visible:ring-inset focus-visible:ring-offset-0"
        />
        {isRolesLoading ? (
          <div className="space-y-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} className="h-10 w-full" />
            ))}
          </div>
        ) : (
          <div className="rounded-md border">
            <div className="flex items-center gap-3 border-b bg-muted/50 p-3">
              <span className="font-medium">Roles</span>
            </div>
            <div className="max-h-[300px] overflow-y-auto">
              {filteredRoles.map((role) => (
                <div
                  key={role.slug}
                  className="flex items-center gap-3 border-b p-3 last:border-b-0 hover:bg-muted/30"
                >
                  <Checkbox
                    checked={selectedRoles.includes(role.slug)}
                    onCheckedChange={() => onRoleToggle(role.slug)}
                  />
                  <span>{role.name}</span>
                </div>
              ))}
              {filteredRoles.length === 0 && (
                <div className="p-4 text-center text-muted-foreground">No roles found</div>
              )}
            </div>
          </div>
        )}
      </div>
    ) : (
      <div className="flex flex-col gap-4">
        <p className="text-sm font-medium">Assigned roles</p>
        <div className="space-y-2">
          {selectedRoles.length === 0 ? (
            <p className="text-sm text-muted-foreground">No roles assigned</p>
          ) : (
            selectedRoles.map((role, index) => (
              <div
                key={role}
                className={`p-3 ${index !== selectedRoles.length - 1 ? "border-b" : ""}`}
              >
                {getRoleDisplayName(role)}
              </div>
            ))
          )}
        </div>
      </div>
    )}
  </TabsContent>
);
