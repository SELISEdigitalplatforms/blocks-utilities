

import { useState } from "react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { useGetSessions } from "@blocks-idp/iam/hooks/use-activity";
import { UserDevicesList } from "./user-devices-list";
import { Pagination } from "@/components/ui-kits/pagination/pagination";

type DevicesProps = {
  id: string;
  projectKey: string;
};

export const UserDevices = ({ id, projectKey }: DevicesProps) => {
  const [filter, setFilter] = useState({ page: 0, pageSize: 10, filter: { UserId: id } });
  const { isLoading, isFetching, data } = useGetSessions({
    ...filter,
    projectKey,
  });
  const loading = isLoading || isFetching;
  return (
    <div className="flex w-full flex-col">
      <Card>
        <CardContent>
          <UserDevicesList isLoading={isLoading || isFetching} data={data?.data || []} />
          {!loading && data && data?.totalCount > filter.pageSize && (
            <div className="mt-5 flex md:justify-end">
              <Pagination
                page={filter.page}
                pageSize={filter.pageSize}
                onChange={(page) => setFilter((filter) => ({ ...filter, page }))}
                totalCount={data?.totalCount || 0}
                pageSizeOptions={[10]}
              />
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
};
