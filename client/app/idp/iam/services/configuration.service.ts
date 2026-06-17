import { serviceInstances } from "@/lib/http-client";
import {
  IIAMConfigurationGetResponse,
  IIAMConfigurationSavePayload,
} from "@blocks-idp/iam/models/configuration.model";
import { IAM_CONFIGURATION_ENDPOINTS } from "../constants/endpoint.constant";

export class ConfigurationService {
  getIamConfiguration(projectKey: string) {
    return serviceInstances.idpService.get<IIAMConfigurationGetResponse>(
      `${IAM_CONFIGURATION_ENDPOINTS.GET}?ProjectKey=${projectKey}`,
      undefined,
      { absoluteUrl: true },
    );
  }

  saveIamConfiguration(payload: IIAMConfigurationSavePayload) {
    return serviceInstances.idpService.post<[]>(IAM_CONFIGURATION_ENDPOINTS.SAVE, { ...payload }, undefined, { absoluteUrl: true });
  }
}

export const configurationService = new ConfigurationService();
