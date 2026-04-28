import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { OrganizationUsersTable } from "./organization-users-table";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { useGetUsers } from "@blocks-idp/iam/hooks/use-user";
import { useProjectStore } from "@/store/useProjectStore";
import {
  OrganizationUsersFilterToolbar,
  useOrganizationUsersFilterQueryParams,
  useOrganizationUsersSortQueryParams,
} from "./organization-users-filter-toolbar";

interface OrganizationUsersProps {
  organizationId: string;
}

export const OrganizationUsers = ({ organizationId }: OrganizationUsersProps) => {
  const { queryParams, setQueryParams } = useOrganizationUsersFilterQueryParams();
  const { sortQueryParams } = useOrganizationUsersSortQueryParams();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  const { isLoading, isFetching, data } = useGetUsers({
    page: queryParams.page,
    pageSize: queryParams.pageSize,
    projectKey: tenantId,
    filter: {
      email: queryParams.email,
      name: queryParams.name,
      organizationId: organizationId,
    },
    sort: sortQueryParams,
  });

  const onPageChangeHandler = (page: number) => {
    setQueryParams((params) => ({ ...params, page }));
  };

  const isUserLoading = isLoading || isFetching;

  return (
    <Card>
      <CardHeader>
        <OrganizationUsersFilterToolbar />
      </CardHeader>

      <CardContent>
        <OrganizationUsersTable users={data?.data || []} isLoading={isUserLoading} />
        {!isUserLoading && data && data.totalCount > queryParams.pageSize && (
          <div className="mt-5 flex items-center md:justify-end">
            <Pagination
              page={queryParams.page}
              pageSize={queryParams.pageSize}
              totalCount={data?.totalCount || 0}
              pageSizeOptions={[10]}
              onChange={onPageChangeHandler}
            />
          </div>
        )}
      </CardContent>
    </Card>
  );
};
