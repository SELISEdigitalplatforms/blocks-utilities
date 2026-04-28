import React, { useState } from "react";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { useMagicUrlsFilterQueryParams, MagicUrlsFilterToolBar } from "./magic-urls-filter-toolbar";
import { MagicUrlsList } from "./magic-urls-list";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetMagicUrls, useSaveMagicUrlConfig } from "@blocks-utilities/hooks/use-magic-url";
import { MagicUrlDialog } from "@blocks-utilities/components/magic-url-dialog/magic-url-dialog";
import { MagicUrlConfigDialog } from "@blocks-utilities/components/magic-url-config-dialog/magic-url-config-dialog";

export const MagicUrls = () => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  const { queryParams, setQueryParams } = useMagicUrlsFilterQueryParams();
  const [isShortenDialogOpen, setIsShortenDialogOpen] = useState(false);
  const [isConfigDialogOpen, setIsConfigDialogOpen] = useState(false);
  const { mutateAsync: saveMagicUrlConfig } = useSaveMagicUrlConfig();

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
    setQueryParams((params) => ({ ...params, page }));
  };

  return (
    <div>
      <div className="mb-[18px] flex w-full flex-row justify-end gap-2 md:mb-[24px]">
        <MagicUrlDialog
          open={isShortenDialogOpen}
          onOpenChange={setIsShortenDialogOpen}
        />
        <MagicUrlConfigDialog
          open={isConfigDialogOpen}
          onOpenChange={setIsConfigDialogOpen}
          projectKey={tenantId}
          onSave={async (config) => {
            await saveMagicUrlConfig(config);
          }}
        />
      </div>

      <Card>
        <CardHeader>
          <MagicUrlsFilterToolBar />
        </CardHeader>
        <CardContent>
          <MagicUrlsList data={data?.data || []} isLoading={isLoading || isFetching} />
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
