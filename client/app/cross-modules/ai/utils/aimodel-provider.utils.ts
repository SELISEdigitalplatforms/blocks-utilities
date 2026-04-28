import { IProvider } from "@blocks-ai/types/aimodel.service.type";

export enum ServicePlatform {
  OFFICIAL_API = "official_api",
  OPEN_DEPLOYMENT = "open_deployment",
  CUSTOM_DEPLOYMENT = "custom_deployment",
}

export const createCustomProvider = (): IProvider => ({
  Provider: "CUSTOM",
  Url: "",
  DocLink: "",
  Description:
    "Bring your own AI model to the platform by connecting external APIs for full customization and control.",
  Order: 999999,
});

export enum ProviderType {
  OFFICIAL = "official",
  OPEN = "open",
  CUSTOM = "custom",
}

export const ProviderToPlatformMap: Record<string, ServicePlatform> = {
  openai: ServicePlatform.OFFICIAL_API,
  anthropic: ServicePlatform.OFFICIAL_API,
  google: ServicePlatform.OFFICIAL_API,
  mistral: ServicePlatform.OFFICIAL_API,
  deepseek: ServicePlatform.OFFICIAL_API,

  azure: ServicePlatform.OPEN_DEPLOYMENT,
  openrouter: ServicePlatform.OPEN_DEPLOYMENT,

  custom: ServicePlatform.OPEN_DEPLOYMENT,
};

export const ProviderNameMap: Record<string, string> = {
  openai: "OpenAI",
  anthropic: "Anthropic",
  google: "Gemini",
  mistral: "Mistral",
  deepseek: "DeepSeek",
  azure: "Azure",
  openrouter: "OpenRouter",
  custom: "Custom Model",
};

export const getProviderDisplayName = (provider: string | undefined): string => {
  if (!provider) return "Provider";
  const mapped = ProviderNameMap[provider.toLowerCase()];
  if (mapped) return mapped;
  return provider.charAt(0).toUpperCase() + provider.slice(1);
};
