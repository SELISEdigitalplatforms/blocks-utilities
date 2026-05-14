import { http } from "@/lib/http-client";
import {
  IIAMConfigurationGetResponse,
  IIAMConfigurationSavePayload,
} from "@blocks-idp/iam/models/configuration.model";
import { IAM_CONFIGURATION_ENDPOINTS } from "../constants/endpoint.constant";

export class ConfigurationService {
  getIamConfiguration(projectKey: string) {
    return http.get<IIAMConfigurationGetResponse>(
      `${IAM_CONFIGURATION_ENDPOINTS.GET}?ProjectKey=${projectKey}`,
    );
  }

  saveIamConfiguration(payload: IIAMConfigurationSavePayload) {
    return http.post<[]>(IAM_CONFIGURATION_ENDPOINTS.SAVE, { ...payload });
  }
}

export const configurationService = new ConfigurationService();
