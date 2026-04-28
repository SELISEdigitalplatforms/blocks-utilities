import { ProviderCard } from "@blocks-ai/components/aimodels/aimodel-card/aimodel-card";
import { IProvider } from "@blocks-ai/types/aimodel.service.type";

interface ProviderListProps {
  providerList: IProvider[];
  servicePlatform: string;
}

export const AiModelsList = ({ servicePlatform, providerList }: ProviderListProps) => {
  const sortedProviderList = [...providerList].sort((a, b) => Number(a.Order) - Number(b.Order));

  const modelsGrid = (providers: IProvider[]) => {
    return (
      <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        {providers.map((provider) => (
          <ProviderCard key={provider.Provider} {...provider} />
        ))}
      </div>
    );
  };

  return (
    <div className="flex flex-col gap-5">
      <p className="border-b-1 border-gray-150 flex w-full border-b pb-2 text-base font-normal text-low-emphasis">
        {servicePlatform}
      </p>
      {providerList.length ? (
        modelsGrid(sortedProviderList)
      ) : (
        <p className="my-4 text-center text-sm text-gray-500">No results</p>
      )}
    </div>
  );
};
