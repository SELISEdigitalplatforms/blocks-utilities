

import { IPermission } from "@blocks-idp/iam/models/permission";
import { AddDependentPermission } from "./add-dependent-permission";
import { Badge } from "@/components/ui-kits/badge/badge";
import { X } from "lucide-react";

type SSOInitialPermissionsProps = {
  permissionsResource: string[];
  onChange: (data: string[]) => void;
};

export function DependentPermissions({
  permissionsResource,
  onChange,
}: SSOInitialPermissionsProps) {
  const onAddHandler = (newPermissions: IPermission[]) => {
    const resources = newPermissions.map((item) => item.resource);
    onChange([...permissionsResource, ...resources]);
  };
  const onRemoveHandler = (permission: string) => {
    onChange(permissionsResource.filter((item) => item !== permission));
  };
  return (
    <div className="flex min-h-10 items-center gap-2 rounded-sm border p-2">
      {permissionsResource.length > 0 && (
        <div className="flex flex-wrap items-center gap-2">
          {permissionsResource.map((item) => (
            <Badge variant="outline" className="w-fit" key={item}>
              {item}
              <X
                className="ml-2 aspect-square w-3 cursor-pointer"
                onClick={() => onRemoveHandler(item)}
              ></X>
            </Badge>
          ))}
        </div>
      )}
      <AddDependentPermission onAdd={onAddHandler} permissionsResource={permissionsResource} />
    </div>
  );
}
