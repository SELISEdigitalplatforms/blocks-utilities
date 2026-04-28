import { useNavigate } from "react-router-dom";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { ChevronRight } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { IProvider } from "@blocks-ai/types/aimodel.service.type";
import { getProviderDisplayName, ProviderType } from "@blocks-ai/utils/aimodel-provider.utils";

const PROVIDER_PNG_MAP: Record<string, string> = {
  google: "/assets/images/google.png",
  deepseek: "/assets/images/deepseek.png",
};

const getProviderPng = (provider: string): string => {
  return PROVIDER_PNG_MAP[provider.toLowerCase()] ?? "";
};

const ProviderIconFallback = ({ provider }: { provider: string }) => {
  const initials = provider.slice(0, 2).toUpperCase();
  return (
    <span className="flex h-8 w-8 items-center justify-center text-sm font-semibold text-muted-foreground">
      {initials}
    </span>
  );
};

export const ProviderCard = (provider: IProvider) => {
  const navigate = useNavigate();
  const pngUrl = getProviderPng(provider.Provider.toLowerCase());

  const handleClick = () => {
    navigate(`/services/secret-management/ai-models/${provider.Provider}`);
  };

  return (
    <Card
      className="w-75 group flex cursor-pointer flex-col items-start gap-4 rounded-md px-4 py-5 transition hover:bg-accent hover:shadow-sm"
      onClick={handleClick}
    >
      <CardHeader className="mb-0 flex w-full flex-row justify-between p-0">
        <div className="flex flex-row">
          <div className="mr-4 flex h-12 w-12 items-center justify-center rounded-sm border p-2">
            {pngUrl ? (
              <img
                src={pngUrl}
                alt={provider.Provider}
                className="h-8 w-8 object-contain"
              />
            ) : (
              <ProviderIconFallback provider={provider.Provider} />
            )}
          </div>
          <div className="flex flex-col">
            <CardTitle className="text-lg font-semibold text-high-emphasis">
              {getProviderDisplayName(provider.Provider)}
            </CardTitle>
            <p className="text-sm font-light text-low-emphasis">
              {provider.Provider.toLowerCase() === ProviderType.CUSTOM ? "My Model" : "My Key"}
            </p>
          </div>
        </div>
        <Button
          variant="ghost"
          size="icon"
          className="p-0 opacity-0 transition-opacity duration-150 group-hover:opacity-100"
          onClick={(e) => { e.stopPropagation(); handleClick(); }}
        >
          <ChevronRight className="aspect-square w-5" />
        </Button>
      </CardHeader>
      <CardContent className="p-0">
        <p className="font-dm line-clamp-3 font-normal text-medium-emphasis">
          {provider.Description}
        </p>
      </CardContent>
    </Card>
  );
};
