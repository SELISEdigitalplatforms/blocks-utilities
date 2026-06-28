import { serviceInstances } from "@/lib/http-client";
import {
  IStorageConfiguration,
  IStorageConfigurationDeletePayload,
  IStorageConfigurationSavePayload,
} from "../models/storage.model";
import { STORAGE_CONFIG_ENDPOINTS } from "../constants/endpoint.constant";

export class StorageConfiguration {
  gets(projectKey: string): Promise<IStorageConfiguration[]> {
    return serviceInstances.logicService.get<IStorageConfiguration[]>(
      `${STORAGE_CONFIG_ENDPOINTS.GET_CONFIGS}?ProjectKey=${projectKey}`,
    );
  }

  save(values: IStorageConfigurationSavePayload): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
    itemId: string;
  }> {
    const url = STORAGE_CONFIG_ENDPOINTS.SAVE_CONFIG;
    const resetValues =
      values.storageStrategy === "Amazon"
        ? {
          host: "",
          port: "",
          userName: "",
          password: "",
          remoteBasePath: "",
          connectionString: "",
        }
        : values.storageStrategy === "Azure"
          ? {
            host: "",
            port: "",
            userName: "",
            password: "",
            accessKey: "",
            secretKey: "",
            cloudStorageRegionEndPoint: "",
          }
          : values.storageStrategy === "S3Compatible"
            ? {
              port: "",
              userName: "",
              password: "",
              remoteBasePath: "",
              connectionString: "",
              cloudStorageRegionEndPoint: "",
            }
            : {
              accessKey: "",
              secretKey: "",
              cloudStorageRegionEndPoint: "",
              connectionString: "",
            };

    // Merge the reset values with the original values
    const payload = { ...resetValues, ...values };

    return serviceInstances.logicService.post(url, payload);
  }

  delete(payload: IStorageConfigurationDeletePayload): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> {
    return serviceInstances.logicService.post(
      `${STORAGE_CONFIG_ENDPOINTS.DELETE_CONFIG}?ProjectKey=${payload.projectKey}&ConfigurationName=${payload.configurationName}`,
      {},
    );
  }
}
