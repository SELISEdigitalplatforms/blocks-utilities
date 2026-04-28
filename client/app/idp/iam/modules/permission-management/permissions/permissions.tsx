

import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { useGetPermissions } from "@blocks-idp/iam/hooks/use-permission";
import { useProjectStore } from "@/store/useProjectStore";
import { PermissionsList } from "./permissions-list";
import {
  PermissionsFilterToolbar,
  usePermissionsFilterQuaryParams,
  usePermissionsSortQuaryParams,
} from "./permissions-filter-toolbar";

import { PermissionsGroupBySeverity } from "./permissions-group-severity";

export function Permissions() {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { queryParams, setQueryParams } = usePermissionsFilterQuaryParams();
  const { sortQueryParams } = usePermissionsSortQuaryParams();
  const { isLoading, isFetching, data } = useGetPermissions({
    ...queryParams,
    sort: sortQueryParams,
    isBuiltIn: queryParams.isBuiltIn === "yes" ? "yes" : queryParams.isBuiltIn === "no" ? "no" : "",
    type: Number(queryParams.type),
    projectKey: tenantId,
    roles: [],
  });

  const onPageChangeHandler = (page: number) => {
    setQueryParams((prev) => ({
      ...prev,
      page,
    }));
  };

  const onPageSizeChangeHandler = (pageSize: number) => {
    setQueryParams((prev) => ({
      ...prev,
      pageSize,
      page: 0,
    }));
  };

  const loading = isLoading || isFetching;
  const permissionsList = data?.data || [];
  const totalCount = data?.totalCount || 0;

  return (
    <div className=" grid gap-4">
      <PermissionsGroupBySeverity />
      <Card className="overflow-hidden">
        <CardHeader>
          <PermissionsFilterToolbar />
        </CardHeader>
        <CardContent>
          <PermissionsList permissions={permissionsList} isLoading={loading} />
          {!loading && (
            <div className="mt-5 flex items-center md:justify-end">
              <Pagination
                page={queryParams.page}
                onChange={onPageChangeHandler}
                totalCount={totalCount}
                pageSizeOptions={[5, 10, 20]}
                onPageSizeChange={onPageSizeChangeHandler}
                pageSize={queryParams.pageSize}
              />
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
