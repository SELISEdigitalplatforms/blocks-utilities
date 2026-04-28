import { useState } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { AddSSORole } from "./add-sso-role";
import { SSORolesList } from "./sso-roles-list";
import { IRole } from "@blocks-idp/iam/models/role";

type SSOInitialRolesProps = {
  roles: IRole[];
  onChange: (data: IRole[]) => void;
};

export const SSOInitialRoles = ({ roles, onChange }: SSOInitialRolesProps) => {
  const [filter, setFilter] = useState({ page: 0, pageSize: 5 });
  const onPageChangeHandler = (page: number) => {
    setFilter((filter) => ({ ...filter, page }));
  };
  const slicedRoles =
    roles.slice(filter.page * filter.pageSize, filter.page * filter.pageSize + filter.pageSize) ||
    [];

  const onAddHandler = (newRoles: IRole[]) => {
    onChange([...roles, ...newRoles]);
  };
  const onRemoveHandler = (role: IRole) => {
    onChange(roles.filter((item) => item.slug !== role.slug));
  };

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <CardTitle>Roles</CardTitle>
        <AddSSORole onAdd={onAddHandler} roles={roles} />
      </CardHeader>
      <CardContent>
        <SSORolesList roles={slicedRoles} onDelete={onRemoveHandler} />
        {roles.length > filter.pageSize && (
          <div className="flex items-center md:justify-end">
            <Pagination
              page={filter.page}
              onChange={onPageChangeHandler}
              totalCount={roles.length || 0}
              pageSizeOptions={[filter.pageSize]}
              pageSize={filter.pageSize}
            />
          </div>
        )}
      </CardContent>
    </Card>
  );
};
