

import { useState } from "react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { useGetHistories } from "@blocks-idp/iam/hooks/use-activity";
import { UserHistoryList } from "./user-history-list";
import { Pagination } from "@/components/ui-kits/pagination/pagination";

type HistoriesProps = {
  id: string;
  projectKey: string;
};

export const UserHistories = ({ id, projectKey }: HistoriesProps) => {
  const [filter, setFilter] = useState({ page: 0, pageSize: 10, filter: { UserId: id } });
  const { isLoading, isFetching, data } = useGetHistories({
    ...filter,
    projectKey: projectKey,
  });
  const loading = isLoading || isFetching;
  return (
    <div className="flex w-full flex-col">
      <Card>
        <CardContent>
          <UserHistoryList isLoading={isLoading || isFetching} data={data?.data || []} />
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
