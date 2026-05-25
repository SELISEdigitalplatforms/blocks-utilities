import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import {
  ScrollArea,
  ScrollBar,
} from "@/components/ui-kits/scroll-area/scroll-area";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui-kits/tabs/tabs";
import { EmailTemplateList } from "@blocks-utilities/mail/pages/email-service-table/email-template-list";
import { EmailUsageList } from "@blocks-utilities/mail/pages/email-usage/email-usage-list";
import { useGetEmailConfigs } from "@blocks-utilities/mail/hooks/use-email-config";
import { useGetEmailTemplates } from "@blocks-utilities/mail/hooks/use-email-template";
import { useGetLanguages } from "@blocks-localization/hooks/use-language-manager";
import { CirclePlus } from "lucide-react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useMemo } from "react";
import { EMAIL_TABS, EmailTabKey } from "@blocks-utilities/mail/constants/email-tabs";
import { useEmailUsageFilterQueryParams } from "../email-usage/email-usage-filter-toolbar";
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
  const { isLoading: isConfigsLoading, data: emailConfigsData } =
    useGetEmailConfigs(0, 100);
  const { isLoading: isLanguageListLoading, data: languageListData } =
    useGetLanguages();
  const navigate = useNavigate();
  const { setQueryParams: setEmailUsageQueryParams } =
    useEmailUsageFilterQueryParams();

  const [searchParams, setSearchParams] = useSearchParams();
  const tabId = searchParams.get("emailAnalytics") || "Emailstemplates";

  const handleTabChange = (value: string) => {
    setSearchParams({ emailAnalytics: value });
    setQueryParams(null);
    setEmailUsageQueryParams(null);
  };

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
      navigate(`/email/communications/${emailId}`);
    }
  };

  const tableData = useMemo(() => {
    if (!data?.templates) return [];
    return data.templates;
  }, [data]);

  return (
    <main className="flex flex-col">
      <div className="flex w-full flex-col">
        <div className="flex w-full justify-between text-high-emphasis">
          <div className="item-center flex gap-2">
            <h3 className="text-2xl font-bold tracking-tight">Email</h3>
          </div>
        </div>
        <Tabs
          value={tabId}
          onValueChange={handleTabChange}
          className="mt-[18px] flex w-full flex-col md:mt-[24px]"
        >
          <div className="mb-5 flex items-center justify-between text-base">
            {/* Mobile Select */}
            <div className="md:hidden">
              <Select
                value={tabId}
                onValueChange={(value) => handleTabChange(value as EmailTabKey)}
              >
                <SelectTrigger className="w-48">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {Object.entries(EMAIL_TABS).map(([key, { label }]) => (
                    <SelectItem key={key} value={key}>
                      {label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            {/* Desktop Tabs */}
            <div className="hidden items-center md:flex">
              <TabsList className="h-[42px] bg-blocks-primary-shades-300">
                {Object.entries(EMAIL_TABS).map(([key, { label }]) => (
                  <TabsTrigger key={key} value={key} className="h-8">
                    {label}
                  </TabsTrigger>
                ))}
              </TabsList>
            </div>

            {tabId === "Emailstemplates" ? (
              <div className="ml-auto flex items-center gap-2">
                <Button
                  size="default"
                  variant="default"
                  className="bg-primary text-primary-foreground shadow-none"
                  onClick={() => navigate("/new-communication")}
                >
                  <CirclePlus className="h-5 w-5 lg:mr-2" />
                  <span className="sr-only lg:not-sr-only">Add Template</span>
                </Button>
              </div>
            ) : null}
          </div>
          <TabsContent value="Emailstemplates">
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
          </TabsContent>
          <TabsContent value="Inbox">
            <Card className="rounded shadow-none">
              <CardContent>
                <EmailUsageList isInbound={true} />
              </CardContent>
            </Card>
          </TabsContent>
          <TabsContent value="Outgoingmails">
            <Card className="rounded shadow-none">
              <CardContent>
                <EmailUsageList isInbound={false} />
              </CardContent>
            </Card>
          </TabsContent>
        </Tabs>
      </div>
    </main>
  );
}
