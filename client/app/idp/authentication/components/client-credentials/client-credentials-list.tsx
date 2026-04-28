
import { ClientCredentialsCard } from "./client-credential-card";
import { useGetAuthClientCredentials } from "@blocks-idp/authentication/hooks/use-auth-clients";
import { useMemo } from "react";
import { useProjectStore } from "@/store/useProjectStore";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";

const LoadingSkeleton = () => (
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

export const ClientCredentialList = () => {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { isLoading, isFetching, data } = useGetAuthClientCredentials({
    projectKey: tenantId,
  });

  const sortedClientsData = useMemo(() => {
    if (!data || data.length === 0) return [];

    return [...data].sort((a, b) => {
      const dateA = new Date(a.createdDate).getTime();
      const dateB = new Date(b.createdDate).getTime();
      return dateB - dateA;
    });
  }, [data]);

  if (isLoading || isFetching) return <LoadingSkeleton />;

  if (!sortedClientsData.length)
    return (
      <div className="text-muted- flex h-32 flex-wrap items-center justify-center rounded-sm border bg-background p-4 text-center">
        No client credential found. Please create a new client credential.
      </div>
    );

  return (
    <div>
      {sortedClientsData?.map((item) => (
        <ClientCredentialsCard key={item.itemId} clientCredential={item} />
      ))}
    </div>
  );
};
