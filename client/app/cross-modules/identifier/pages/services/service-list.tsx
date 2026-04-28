import { ServiceCard } from "@blocks-identifier/components/service-card/service-card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { useGetAllServices } from "@blocks-identifier/hooks/use-services";
import { useProjectStore } from "@/store/useProjectStore";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { parseAsInteger, useQueryStates } from "nuqs";
import { Accordion, AccordionItem } from "@/components/ui-kits/accordion/accordion";

const ServiceListSkeleton = () => (
  <div className="grid gap-4">
    {Array.from({ length: 3 }).map((_, index) => (
      <Card key={index}>
        <CardContent>
          <Skeleton className="h-6 w-1/2" />
          <Skeleton className="mt-2 h-12" />
          <Skeleton className="mt-2 h-12" />
        </CardContent>
      </Card>
    ))}
  </div>
);

const EmptyServiceList = () => (
  <Card className="p-8 text-center">
    <CardContent>
      <div className="text-muted-foreground">
        <p className="mb-2 text-lg font-medium">No services found</p>
        <p className="text-sm">Register your first service to get started</p>
      </div>
    </CardContent>
  </Card>
);

export const ServiceList = () => {
  const [queryParams, setQueryParams] = useQueryStates({
    page: parseAsInteger.withDefault(0),
    pageSize: parseAsInteger.withDefault(10),
  });
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  const { data, isLoading, isFetching } = useGetAllServices({
    projectKey: tenantId,
    page: queryParams.page,
    pageSize: queryParams.pageSize,
  });

  const onPageChangeHandler = (page: number) => {
    setQueryParams((params) => ({ ...params, page }));
  };

  const isServiceLoading = isLoading || isFetching;

  if (isServiceLoading) return <ServiceListSkeleton />;
  if (!data?.totalCount) return <EmptyServiceList />;

  return (
    <>
      <Accordion type="single" collapsible className="grid grid-cols-1 gap-y-4">
        {data?.data.map((service) => (
          <AccordionItem
            value={service.itemId}
            key={service.itemId}
            className="rounded-sm border bg-background"
          >
            <ServiceCard
              key={service.itemId}
              service={{
                ...service,
                metadata: service.metadata ?? {},
              }}
            />
          </AccordionItem>
        ))}
      </Accordion>

      {!isServiceLoading && data && data.totalCount > queryParams.pageSize && (
        <div className="flex items-center md:justify-end">
          <Pagination
            page={queryParams.page}
            pageSize={queryParams.pageSize}
            totalCount={data?.totalCount || 0}
            onChange={onPageChangeHandler}
          />
        </div>
      )}
    </>
  );
};
