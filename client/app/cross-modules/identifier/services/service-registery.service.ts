import { HttpClient } from "@/lib/http-client";
import {
  IRegisterServicePayload,
  IRegisterServiceResponse,
  IGetAllServicesPayload,
  IGetAllServicesResponse,
} from "../types/services.type";
import { SERVICE_REGISTRY_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";
import { getRuntimeEnv } from "@/lib/runtime-env";

const logicHttp = new HttpClient(
  getRuntimeEnv("BLOCKS_LOGIC_BASE_URL") || "",
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class ServiceRegistryService {
  registerService(payload: IRegisterServicePayload): Promise<IRegisterServiceResponse> {
    return logicHttp.post(SERVICE_REGISTRY_ENDPOINTS.REGISTER, payload);
  }

  getAllServices(payload: IGetAllServicesPayload): Promise<IGetAllServicesResponse> {
    return logicHttp.post(SERVICE_REGISTRY_ENDPOINTS.GET_ALL, payload);
  }
}

export const serviceRegistryService = new ServiceRegistryService();
