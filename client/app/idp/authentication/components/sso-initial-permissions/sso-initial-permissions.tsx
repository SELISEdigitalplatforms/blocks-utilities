
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";

import { IPermission } from "@blocks-idp/iam/models/permission";
import { AddSSOPermission } from "./add-sso-permission";
import { SSOPermissionsList } from "./sso-permissions-list";

type SSOInitialPermissionsProps = {
  permissions: IPermission[];
  onChange: (data: IPermission[]) => void;
};

export function SSOInitialPermissions({ permissions, onChange }: SSOInitialPermissionsProps) {
  const onAddHandler = (newPermissions: IPermission[]) => {
    onChange([...permissions, ...newPermissions]);
  };
  const onRemoveHandler = (permission: IPermission) => {
    onChange(permissions.filter((item) => item.resource !== permission.resource));
  };
  return (
    <div>
      <div className="flex w-full flex-col">
        <Card>
          <CardHeader className="flex !flex-row items-center justify-between">
            <CardTitle>Permissions</CardTitle>
            <AddSSOPermission onAdd={onAddHandler} permissions={permissions} />
          </CardHeader>
          <CardContent>
            <SSOPermissionsList permissions={permissions} onDelete={onRemoveHandler} />
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
