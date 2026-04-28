

import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { ScrollArea, ScrollBar } from "@/components/ui-kits/scroll-area/scroll-area";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { EmailTemplateList } from "@blocks-communication/mail/email/email-service-table/email-template-list";
import { useGetEmailConfigs } from "@blocks-communication/mail/hooks/use-email-config";
import { useGetEmailTemplates } from "@blocks-communication/mail/hooks/use-email-template";
import { useGetLanguages } from "@blocks-localization/hooks/use-language-manager";
import { useNavigate } from "react-router-dom";
import { useMemo } from "react";
import {
  TemplateFilterToolbar,
  useTemplatesFilterQueryParams,
  useTemplatesSortQueryParams,
} from "./template-filter-toolbar";

const LoadingSkelton = () => {
  return (
    <div className="grid gap-2">
      {Array.from({ length: 6 }).map((_, index) => (
        <Skeleton key={index} className="h-12 w-full rounded" />
      ))}
    </div>
  );
};

interface EmailServiceTableProps {
  onRowClick?: (id: string | number) => void;
}

export function EmailServiceTable({ onRowClick }: EmailServiceTableProps = {}) {
  const { queryParams, setQueryParams } = useTemplatesFilterQueryParams();
  const { sortQueryParams } = useTemplatesSortQueryParams();
  const { isLoading, data } = useGetEmailTemplates(
    queryParams.pageNumber ?? 0,
    queryParams.pageSize ?? 10,
    queryParams.search ?? "",
    sortQueryParams.property ?? "Name",
    sortQueryParams.isDescending ?? false,
    queryParams.language ?? "",
    queryParams.mailConfigurationId ?? "",
  );
  const { isLoading: isConfigsLoading, data: emailConfigsData } = useGetEmailConfigs(0, 100);
  const { isLoading: isLanguageListLoading, data: languageListData } = useGetLanguages();
  const navigate = useNavigate();

  const onPageChangeHandler = (pageNumber: number) => {
    setQueryParams((prev) => ({
      ...prev,
      pageNumber,
    }));
  };

  const handleRowClick = (emailId: number | string) => {
    if (onRowClick) {
      onRowClick(emailId);
    } else {
      navigate(`/utilities/email/communications/${emailId}`);
    }
  };

  const tableData = useMemo(() => {
    if (!data?.templates) return [];
    return data.templates;
  }, [data]);

  return (
    <main className="flex flex-col">
      <Card className="rounded shadow-none">
        <CardContent className="mb-4">
          {isConfigsLoading || isLanguageListLoading ? (
            <Skeleton className="h-12 w-full rounded" />
          ) : (
            <TemplateFilterToolbar
              emailConfigsData={emailConfigsData || []}
              languageListData={languageListData || []}
            />
          )}
        </CardContent>
        <CardContent>
          <ScrollArea className="w-full">
            {isLoading || isConfigsLoading ? (
              <LoadingSkelton />
            ) : (
              <EmailTemplateList
                templates={tableData}
                isLoading={isLoading}
                emailConfigsData={emailConfigsData || []}
                onRowClick={handleRowClick}
              />
            )}
            <ScrollBar orientation="horizontal" />
          </ScrollArea>
        </CardContent>
        {!isLoading && data && data.totalCount > queryParams.pageSize && (
          <div className="mt-5 flex items-center md:justify-end">
            <Pagination
              page={queryParams.pageNumber}
              pageSize={queryParams.pageSize}
              totalCount={data?.totalCount || 0}
              pageSizeOptions={[10]}
              onChange={onPageChangeHandler}
            />
          </div>
        )}
      </Card>
    </main>
  );
}
