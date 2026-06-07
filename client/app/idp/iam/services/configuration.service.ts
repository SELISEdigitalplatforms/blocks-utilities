import { HttpClient } from "@/lib/http-client";
import {
  IIAMConfigurationGetResponse,
  IIAMConfigurationSavePayload,
} from "@blocks-idp/iam/models/configuration.model";
import { IAM_CONFIGURATION_ENDPOINTS } from "../constants/endpoint.constant";
import { deriveIdpBaseUrl } from "@/lib/blocks-url.util";
import { getRuntimeEnv } from "@/lib/runtime-env";

const iamHttp = new HttpClient(
  deriveIdpBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class ConfigurationService {
  getIamConfiguration(projectKey: string) {
    return iamHttp.get<IIAMConfigurationGetResponse>(
      `${IAM_CONFIGURATION_ENDPOINTS.GET}?ProjectKey=${projectKey}`,
      undefined,
      { absoluteUrl: true },
    );
  }

  saveIamConfiguration(payload: IIAMConfigurationSavePayload) {
    return iamHttp.post<[]>(IAM_CONFIGURATION_ENDPOINTS.SAVE, { ...payload }, undefined, { absoluteUrl: true });
  }
}

export const configurationService = new ConfigurationService();
