import { http } from "@/lib/http-client";
import {
  IRegisterServicePayload,
  IRegisterServiceResponse,
  IGetAllServicesPayload,
  IGetAllServicesResponse,
} from "../types/services.type";
import { SERVICE_REGISTRY_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";

export class ServiceRegistryService {
  registerService(payload: IRegisterServicePayload): Promise<IRegisterServiceResponse> {
    return http.post(SERVICE_REGISTRY_ENDPOINTS.REGISTER, payload);
  }

  getAllServices(payload: IGetAllServicesPayload): Promise<IGetAllServicesResponse> {
    return http.post(SERVICE_REGISTRY_ENDPOINTS.GET_ALL, payload);
  }
}

export const serviceRegistryService = new ServiceRegistryService();
