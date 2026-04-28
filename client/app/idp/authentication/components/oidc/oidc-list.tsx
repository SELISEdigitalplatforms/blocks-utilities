
import { useGetAuthOidcCredentials } from "@blocks-idp/authentication/hooks/use-auth-oidc";
import { OIDCCard } from "./oidc-card";
import { useMemo } from "react";
import { useProjectStore } from "@/store/useProjectStore";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";

const LoadingSkeleton = () => {
  return (
    <Card className="py-6">
      <CardHeader>
        <div className="flex items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <Skeleton className="h-6 w-40 rounded" />
            <Skeleton className="h-5 w-14 rounded" />
          </div>
          <Skeleton className="h-8 w-20 rounded" />
        </div>
      </CardHeader>

      <CardContent>
        <div className="flex flex-col gap-8">
          <div className="grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-3">
            <div className="min-w-0">
              <Skeleton className="mb-2 h-4 w-24 rounded" />
              <Skeleton className="h-5 w-40 rounded" />
            </div>

            <div className="min-w-0">
              <Skeleton className="mb-2 h-4 w-28 rounded" />
              <Skeleton className="h-5 w-40 rounded" />
            </div>

            <div className="min-w-0">
              <Skeleton className="mb-2 h-4 w-24 rounded" />
              <Skeleton className="h-5 w-32 rounded" />
            </div>

            <div className="min-w-0">
              <Skeleton className="mb-2 h-4 w-20 rounded" />
              <div className="flex gap-2">
                <Skeleton className="h-6 w-16 rounded" />
                <Skeleton className="h-6 w-16 rounded" />
              </div>
            </div>

            <div className="min-w-0">
              <Skeleton className="mb-2 h-4 w-24 rounded" />
              <Skeleton className="h-5 w-32 rounded" />
            </div>
          </div>
        </div>
      </CardContent>
    </Card>
  );
};

export const OidcList = () => {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { isLoading, isFetching, data } = useGetAuthOidcCredentials({
    projectKey: tenantId,
  });

  const sortedOidcData = useMemo(() => {
    if (!data || !data.oIDCClientCredentials) return [];
    const dataArray = Array.isArray(data.oIDCClientCredentials)
      ? data.oIDCClientCredentials
      : [data.oIDCClientCredentials];
    if (dataArray.length === 0) return [];
    return [...dataArray].sort((a, b) => {
      const dateA = new Date(a.createdDate).getTime();
      const dateB = new Date(b.createdDate).getTime();
      return dateB - dateA;
    });
  }, [data]);

  if (isLoading || isFetching) return <LoadingSkeleton />;

  if (!sortedOidcData.length)
    return (
      <div className="text-muted- flex h-32 flex-wrap items-center justify-center rounded-sm border bg-background p-4 text-center">
        No OIDC configuration found. Please create a new OIDC configuration.
      </div>
    );

  return (
    <div className="grid gap-4">
      {sortedOidcData?.map((item) => <OIDCCard key={item.itemId} oidc={item} />)}
    </div>
  );
};
