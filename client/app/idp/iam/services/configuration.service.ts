import { http, HttpClient } from "@/lib/http-client";
import {
  IIAMConfigurationGetResponse,
  IIAMConfigurationSavePayload,
} from "@blocks-idp/iam/models/configuration.model";
import { IAM_CONFIGURATION_ENDPOINTS } from "../constants/endpoint.constant";
import { deriveLogicBaseUrl } from "@/lib/blocks-url.util";
import { getRuntimeEnv } from "@/lib/runtime-env";

const logicHttp = new HttpClient(
  deriveLogicBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class ConfigurationService {
  getIamConfiguration(projectKey: string) {
    return logicHttp.get<IIAMConfigurationGetResponse>(
      `${IAM_CONFIGURATION_ENDPOINTS.GET}?ProjectKey=${projectKey}`,
    );
  }

  saveIamConfiguration(payload: IIAMConfigurationSavePayload) {
    return logicHttp.post<[]>(IAM_CONFIGURATION_ENDPOINTS.SAVE, { ...payload });
  }
}

export const configurationService = new ConfigurationService();
