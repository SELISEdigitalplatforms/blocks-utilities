import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { UsersTable } from "./users-table";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { useGetUsers } from "@blocks-idp/iam/hooks/use-user";
import { useProjectStore } from "@/store/useProjectStore";
import {
  UsersFilterToolbar,
  useUsersFilterQueryParams,
  useUsersSortQueryParams,
} from "./users-filter-toolbar";

export const Users = () => {
  const { queryParams, setQueryParams } = useUsersFilterQueryParams();
  const { sortQueryParams } = useUsersSortQueryParams();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  const { isLoading, isFetching, data } = useGetUsers({
    page: queryParams.page,
    pageSize: queryParams.pageSize,
    projectKey: tenantId,
    filter: { email: queryParams.email, name: queryParams.name },
    sort: sortQueryParams,
  });

  const onPageChangeHandler = (page: number) => {
    setQueryParams((params) => ({ ...params, page }));
  };

  const isUserLoading = isLoading || isFetching;

  return (
    <Card>
      <CardHeader>
        <UsersFilterToolbar />
      </CardHeader>

      <CardContent>
        <UsersTable users={data?.data || []} isLoading={isUserLoading} />
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
