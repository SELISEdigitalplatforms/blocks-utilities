import React from "react";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import {
  useMagicUrlsFilterQueryParams,
  MagicUrlsFilterToolBar,
} from "./magic-urls-filter-toolbar";
import { MagicUrlsList } from "./magic-urls-list";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetMagicUrls } from "@blocks-utilities/magic-url/hooks/use-magic-url";

export const MagicUrls = () => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  const { queryParams, setQueryParams } = useMagicUrlsFilterQueryParams();

  const { data, isLoading, isFetching } = useGetMagicUrls({
    page: queryParams.page,
    pageSize: queryParams.pageSize,
    projectKey: tenantId,
    searchText: queryParams.search || undefined,
    status: queryParams.status || undefined,
    expiryDateRangeStartDate: queryParams.expiryStartDate || undefined,
    expiryDateRangeEndDate: queryParams.expiryEndDate || undefined,
    requestMethod: queryParams.requestMethod || undefined,
    type: queryParams.type || undefined,
  });

  const loading = isLoading || isFetching;

  const pageChangeHandler = (page: number) => {
    setQueryParams((params: { page: number; pageSize: number }) => ({ ...params, page }));
  };

  return (
    <div>
      <Card>
        <CardHeader>
          <MagicUrlsFilterToolBar />
        </CardHeader>
        <CardContent>
          <MagicUrlsList
            data={data?.data || []}
            isLoading={isLoading || isFetching}
          />
          {!loading && data && data.totalCount > queryParams.pageSize && (
            <div className="mt-5 flex items-center md:justify-end">
              <Pagination
                page={queryParams.page}
                pageSize={queryParams.pageSize}
                pageSizeOptions={[queryParams.pageSize]}
                onChange={pageChangeHandler}
                totalCount={data?.totalCount || 0}
              />
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
};
