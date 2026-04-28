import { Card } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { AiModelsList } from "./aimodels-list";
import { useAIModelsQueryParams, useSeedProviders } from "@blocks-ai/hooks/use-aimodel";
import { AIModelsFilterToolbar } from "@blocks-ai/components/aimodels/aimodel-filter-toolbar/aimodel-filter-toolbar";
import { IProvider } from "@blocks-ai/types/aimodel.service.type";
import {
  createCustomProvider,
  ProviderToPlatformMap,
  ServicePlatform,
} from "@blocks-ai/utils/aimodel-provider.utils";

const ProviderGridSkeleton = () => {
  return (
    <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
      {Array.from({ length: 4 }).map((_, index) => (
        <div key={index} className="flex flex-col gap-4 rounded-md border px-4 py-5">
          <div className="mb-0 flex w-full flex-row justify-between">
            <div className="flex flex-row gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-sm border p-2">
                <Skeleton className="h-8 w-8" />
              </div>
              <div className="flex flex-col gap-2">
                <Skeleton className="h-5 w-32" />
                <Skeleton className="h-4 w-20" />
              </div>
            </div>
            <Skeleton className="h-5 w-5" />
          </div>
          <div>
            <Skeleton className="mb-1 h-4 w-full" />
            <Skeleton className="mb-1 h-4 w-5/6" />
            <Skeleton className="h-4 w-2/3" />
          </div>
        </div>
      ))}
    </div>
  );
};

export const AIModels = () => {
  const { data: providers = [], isLoading } = useSeedProviders();

  const allProviders = [...providers, createCustomProvider()];

  const officialApiModels = allProviders.filter((p) => {
    if (!p || !p.Provider) return false;
    return ProviderToPlatformMap[p.Provider.toLowerCase()] === ServicePlatform.OFFICIAL_API;
  });

  const openDeploymentModels = allProviders.filter((p) => {
    if (!p || !p.Provider) return false;
    return ProviderToPlatformMap[p.Provider.toLowerCase()] !== ServicePlatform.OFFICIAL_API;
  });

  const { queryParams } = useAIModelsQueryParams();
  const search = queryParams.search.toLowerCase();
  const selectedTypes = queryParams.types;

  const matchSearch = (p: IProvider) => p.Provider.toLowerCase().includes(search);

  const shouldShowOfficial = selectedTypes.length === 0 || selectedTypes.includes("official");
  const shouldShowOpen = selectedTypes.length === 0 || selectedTypes.includes("open");

  const filteredOfficial = officialApiModels.filter(matchSearch);
  const filteredOpen = openDeploymentModels.filter(matchSearch);

  return (
    <Card className="flex flex-col gap-5 p-5">
      <AIModelsFilterToolbar />

      {isLoading ? (
        <div className="flex flex-col gap-5">
          {shouldShowOfficial && (
            <div className="flex flex-col gap-5">
              <p className="flex w-full border-b pb-2 text-base font-normal text-low-emphasis">
                Official API
              </p>
              <ProviderGridSkeleton />
            </div>
          )}
          {shouldShowOpen && (
            <div className="flex flex-col gap-5">
              <p className="flex w-full border-b pb-2 text-base font-normal text-low-emphasis">
                Open Deployment
              </p>
              <ProviderGridSkeleton />
            </div>
          )}
        </div>
      ) : (
        <>
          {shouldShowOfficial && (
            <AiModelsList servicePlatform="Official API" providerList={filteredOfficial} />
          )}
          {shouldShowOpen && (
            <AiModelsList servicePlatform="Open Deployment" providerList={filteredOpen} />
          )}
        </>
      )}
    </Card>
  );
};
