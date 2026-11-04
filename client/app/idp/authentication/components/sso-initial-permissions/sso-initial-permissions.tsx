
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";

import { IPermission } from "@blocks-idp/iam/models/permission";
import { AddSSOPermission } from "./add-sso-permission";
import { SSOPermissionsList } from "./sso-permissions-list";
import { useT } from "@seliseblocks/genesis-os";

type SSOInitialPermissionsProps = {
  permissions: IPermission[];
  onChange: (data: IPermission[]) => void;
};

export function SSOInitialPermissions({ permissions, onChange }: SSOInitialPermissionsProps) {
  const { t } = useT("utilities.idp.authentication");
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
            <CardTitle>{t("SSO_INITIAL_PERMISSIONS.PERMISSIONS")}</CardTitle>
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
