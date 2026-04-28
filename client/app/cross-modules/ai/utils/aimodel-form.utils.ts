import { ICreateModelPayload } from "@blocks-ai/types/aimodel.service.type";
import { z } from "zod";
import { ProviderToPlatformMap, ServicePlatform } from "./aimodel-provider.utils";

export const OpenAIModelSchema = z.object({
  model: z.string().trim().min(1, "Model is required"),
  url: z.string().trim().url("Enter a valid URL"),
  apiKey: z.string().min(1, "API key is required"),
  organizationId: z.string().trim().optional(),
  projectId: z.string().trim().optional(),
});
type FormSchemaOpenai = z.infer<typeof OpenAIModelSchema>;
export const OpenAIModelDefaults = (defaultModel: string): FormSchemaOpenai => ({
  model: defaultModel,
  url: "",
  apiKey: "",
  organizationId: "",
  projectId: "",
});

export const OfficialApiModelSchema = z.object({
  model: z.string().trim().min(1, "Model is required"),
  url: z.string().trim().url("Enter a valid URL"),
  apiKey: z.string().min(1, "API key is required"),
});
type FormSchemaOfficialApi = z.infer<typeof OfficialApiModelSchema>;
export const OfficialApiModelDefaults = (defaultModel: string): FormSchemaOfficialApi => ({
  model: defaultModel,
  url: "",
  apiKey: "",
});

export const OpenDeploymentModelSchema = z.object({
  model: z.string().trim().min(1, "Model is required"),
  url: z.string().trim().url("Enter a valid URL"),
  deploymentName: z.string().trim().min(1, "Deployment name is required"),
  apiKey: z.string().min(1, "API key is required"),
});
type FormSchemaOpenDeployment = z.infer<typeof OpenDeploymentModelSchema>;
export const OpenDeploymentDefaults = (defaultModel: string): FormSchemaOpenDeployment => ({
  model: defaultModel,
  url: "-",
  deploymentName: "-",
  apiKey: "",
});

export interface UniversalFormInput {
  version?: string;
  model?: string;
  displayName?: string;
  apiKey?: string;
  url?: string;
  organizationId?: string;
  projectId?: string;
  apiVersion?: string;
  deploymentName?: string;
  DefaultTemp?: number;
  MaxTokens?: number;
  customParameters?: Record<string, unknown>;
  customHeaders?: Record<string, string>;
}

export const transformToUniversal = (
  provider: string,
  project_key: string,
  form: UniversalFormInput,
): ICreateModelPayload => {
  return {
    provider,
    service_platform: ProviderToPlatformMap[provider],
    version: form.version ?? "",
    model_name: form.model ?? "",
    display_name: form.displayName ?? "",
    api_key: form.apiKey ?? "",
    base_url: form.url ?? "",
    openai_organization_id: form.organizationId ?? "",
    openai_project_id: form.projectId ?? "",
    api_version: form.apiVersion ?? "",
    deployment_name: form.deploymentName ?? "",
    project_key: project_key,
    DefaultTemp: form.DefaultTemp ?? 0.3,
    MaxTokens: form.MaxTokens ?? 10922,
    custom_parameters: form.customParameters ?? {},
    custom_headers: form.customHeaders ?? {},
  };
};

export const resolveModelConfig = (
  provider: string,
  servicePlatform: ServicePlatform,
  modelOptions: { model: string; goodName: string }[],
) => {
  const defaultModel = modelOptions[0]?.model ?? "--";

  if (provider === "openai") {
    return {
      schema: OpenAIModelSchema,
      defaultValues: OpenAIModelDefaults(defaultModel),
      fields: ["model", "url", "apiKey", "organizationId", "projectId"],
    };
  }

  if (servicePlatform === ServicePlatform.OPEN_DEPLOYMENT) {
    return {
      schema: OpenDeploymentModelSchema,
      defaultValues: OpenDeploymentDefaults(defaultModel),
      fields: ["model", "url", "deploymentName", "apiKey"],
    };
  }

  return {
    schema: OfficialApiModelSchema,
    defaultValues: OfficialApiModelDefaults(defaultModel),
    fields: ["model", "url", "apiKey"],
  };
};
