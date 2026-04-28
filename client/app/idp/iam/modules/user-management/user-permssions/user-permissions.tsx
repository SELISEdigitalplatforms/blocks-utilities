
import { useState, useEffect } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { UserPermissionsList } from "./user-permissions-list";
import { useUserPermissions } from "@blocks-idp/iam/hooks/use-user";
import { AddUserPermission } from "./add-user-permission";
import { Button } from "@/components/ui-kits/button/button";
import { toast } from "@/hooks/use-toast";
import { IPermission } from "@blocks-idp/iam/models/permission";

type UserPermissionsProps = {
  userId: string;
  projectKey: string;
};

export function UserPermissions({ userId, projectKey }: UserPermissionsProps) {
  const { permissions, isLoading, deletePermissions } = useUserPermissions({ userId, projectKey });

  const [localPermissions, setLocalPermissions] = useState<IPermission[]>([]);
  const [removedResources, setRemovedResources] = useState<string[]>([]);
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    setLocalPermissions(permissions);
    setRemovedResources([]);
  }, [permissions]);

  const onRemovePermission = (resource: string) => {
    setLocalPermissions((prev) => prev.filter((perm) => perm.resource !== resource));
    setRemovedResources((prev) => [...prev, resource]);
  };

  const onReset = () => {
    setLocalPermissions(permissions);
    setRemovedResources([]);
  };

  const onSave = async () => {
    if (!removedResources.length) return;
    setIsSaving(true);
    try {
      const res = await deletePermissions(removedResources);
      if (res.isSuccess) {
        toast({
          variant: "success",
          title: "Success",
          description: "Permissions updated successfully",
        });
      } else {
        toast({
          variant: "destructive",
          title: "Error",
          description: res.errors as string || "Something went wrong",
        });
      }
    } catch (error) {
      toast({
        variant: "destructive",
        title: "Error",
        description: `Something went wrong | ${JSON.stringify(error)}`,
      });
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div>
      <div className="flex w-full flex-col">
        <Card>
          <CardHeader className="flex !flex-row items-center justify-between">
            <CardTitle>Permissions</CardTitle>
            <div className="flex gap-2">
              {!!removedResources.length && (
                <>
                  <Button variant="outline" onClick={onReset} disabled={isSaving || (!removedResources.length && localPermissions.length === permissions.length)}>
                    Reset
                  </Button>
                  <Button variant="outline" onClick={onSave} disabled={isSaving || !removedResources.length}>
                    {isSaving ? "Saving..." : "Save"}
                  </Button>
                </>
              )}
              <AddUserPermission userId={userId} projectKey={projectKey} />
            </div>
          </CardHeader>
          <CardContent>
            <UserPermissionsList
              permissions={localPermissions}
              isLoading={isLoading}
              userId={userId}
              onRemovePermission={onRemovePermission}
            />
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
