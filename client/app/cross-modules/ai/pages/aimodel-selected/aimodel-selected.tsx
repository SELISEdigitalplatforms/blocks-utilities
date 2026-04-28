import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { AIModelsTable } from "@blocks-ai/components/aimodels/aimodel-table/aimodel-table";
import { CustomModelAddKeyModal } from "@blocks-ai/components/aimodels/modals/aimodel-addkey-modal-custom/aimodel-addkey-modal-custom";
import { ModelAddKeyModal } from "@blocks-ai/components/aimodels/modals/aimodel-addkey-modal/aimodel-addkey-modal";
import { AIModelsSelectedPageFilterToolbar } from "@blocks-ai/components/aimodels/aimodel-selectedpage-filter-toolbar/aimodel-selectedpage-filter-toolbar";
import {
  createCustomProvider,
  getProviderDisplayName,
  ProviderToPlatformMap,
  ProviderType,
  ServicePlatform,
} from "@blocks-ai/utils/aimodel-provider.utils";
import {
  useAIModelsQueryParams,
  useGetModels,
  useSeedModelsByProvider,
  useSeedProviders,
} from "@blocks-ai/hooks/use-aimodel";
import { useProjectStore } from "@/store/useProjectStore";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { Plus, ArrowLeft } from "lucide-react";

const PROVIDER_PNG_MAP: Record<string, string> = {
  google: "/assets/images/google.png",
  deepseek: "/assets/images/deepseek.png",
};

type AIModelSelectedPageProps = {
  provider: string;
};

export const AIModelSelectedPage = ({ provider }: AIModelSelectedPageProps) => {
  const navigate = useNavigate();
  const project_key = useProjectStore().selectedProject?.tenantId || "";

  const { data: providers } = useSeedProviders();

  const servicePlatform = ProviderToPlatformMap[provider.toLowerCase()] as ServicePlatform;

  const description =
    providers && Array.isArray(providers) && provider
      ? provider.toLowerCase() === ProviderType.CUSTOM
        ? createCustomProvider().Description
        : (providers.find((p) => p.Provider && p.Provider.toLowerCase() === provider.toLowerCase())
            ?.Description ?? "")
      : "";

  const { data: seedModels, isLoading: isSeedLoading } = useSeedModelsByProvider(provider);
  const baseUrl = seedModels && seedModels.length > 0 ? seedModels[0].DefaultBaseUrl || "" : "";

  type ModelOption = { model: string; goodName: string };
  const modelOptions: ModelOption[] =
    seedModels?.map((m) => ({
      model: m.Model,
      goodName: m.ModelGoodName ?? m.Model,
    })) ?? [];

  const {
    queryParams: { search, page, page_size },
    setQueryParams,
  } = useAIModelsQueryParams();

  const {
    data: models,
    isLoading: isModelsLoading,
    isFetching: isModelsFetching,
  } = useGetModels(
    {
      provider: provider.toLowerCase(),
      search: search || null,
      page,
      page_size,
    },
    project_key,
  );

  const loading = isModelsLoading || isModelsFetching;
  const totalCount: number = models?.total ?? 0;
  const currentPage: number = page ?? models?.page ?? 1;

  const pngUrl = PROVIDER_PNG_MAP[provider.toLowerCase()] ?? "";

  const [addKeyModalOpen, setAddKeyModalOpen] = useState(false);

  return (
    <div className="p-6">
      <div className="mb-4 flex items-center gap-2">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/services/secret-management?tab=ai-models")}
          className="gap-1 pl-0"
        >
          <ArrowLeft className="h-4 w-4" />
          Back
        </Button>
      </div>

      <div className="my-4 flex flex-row">
        <div className="mr-4 flex h-12 w-12 items-center justify-center rounded-sm border p-2">
          {pngUrl ? (
            <img src={pngUrl} alt={provider} className="h-8 w-8 object-contain" />
          ) : (
            <span className="flex h-8 w-8 items-center justify-center text-sm font-semibold text-muted-foreground">
              {provider.slice(0, 2).toUpperCase()}
            </span>
          )}
        </div>
        <div className="flex flex-col">
          <h4 className="text-lg font-semibold md:text-xl">{getProviderDisplayName(provider)}</h4>
          <p className="text-medium-emphasis">{description}</p>
        </div>
      </div>

      <div className="flex flex-row gap-5">
        <Card className="flex w-full flex-col gap-5 p-5">
          <CardHeader className="mb-0 flex w-full flex-row justify-between p-0">
            <CardTitle className="text-lg font-semibold text-high-emphasis">
              Self-owned model
            </CardTitle>
            <Button
              variant="outline"
              className="px-2 py-1 sm:px-4 sm:py-2"
              onClick={() => {
                if (!isSeedLoading) setAddKeyModalOpen(true);
              }}
            >
              <Plus className="mr-2 h-4 w-4" />
              Add Model
            </Button>
          </CardHeader>

          <CardContent className="p-0">
            <div className="mb-4">
              <AIModelsSelectedPageFilterToolbar />
            </div>

            <AIModelsTable
              custom={provider.toLowerCase() === ProviderType.CUSTOM}
              models={models?.models || []}
              isLoading={loading}
            />

            {!loading && totalCount > page_size && (
              <div className="mt-4 flex items-center justify-end">
                <Pagination
                  page={currentPage - 1}
                  pageSize={page_size}
                  pageSizeOptions={[page_size]}
                  totalCount={totalCount}
                  onChange={(nextPageZeroBased) =>
                    setQueryParams((prev) => ({ ...prev, page: nextPageZeroBased + 1 }))
                  }
                  onPageSizeChange={(newPageSize) =>
                    setQueryParams((prev) => ({ ...prev, page_size: newPageSize, page: 1 }))
                  }
                />
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      {provider.toLowerCase() === ProviderType.CUSTOM ? (
        <CustomModelAddKeyModal
          addKeyModalOpen={addKeyModalOpen}
          setAddKeyModalOpen={setAddKeyModalOpen}
        />
      ) : (
        <ModelAddKeyModal
          key={baseUrl}
          provider={provider}
          modelOptions={modelOptions}
          baseUrl={baseUrl}
          servicePlatform={servicePlatform}
          addKeyModalOpen={addKeyModalOpen}
          setAddKeyModalOpen={setAddKeyModalOpen}
        />
      )}
    </div>
  );
};
