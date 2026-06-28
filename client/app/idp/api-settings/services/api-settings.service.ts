import { serviceInstances } from "@/lib/http-client";
import { API_SETTINGS_ENDPOINTS } from "../constants/endpoint.constant";
import {
  IGetApiEndpointsPayload,
  IGetApiEndpointsResponse,
  IUpdateApiEndpointPayload,
  IUpdateApiEndpointResponse,
  IBulkUpdateApiEndpointsPayload,
  IBulkUpdateApiEndpointsResponse,
  IRemoveApiEndpointsPayload,
  IRemoveApiEndpointsResponse,
} from "../models/api-endpoint.model";

class ApiSettingsService {
  getEndpoints(payload: IGetApiEndpointsPayload): Promise<IGetApiEndpointsResponse> {
    return serviceInstances.idpService.post(API_SETTINGS_ENDPOINTS.GET_LIST, {
      projectKey: payload.projectKey,
      page: payload.page ?? 0,
      pageSize: payload.pageSize ?? 100,
      filter: payload.filter ?? {},
    });
  }

  updateEndpoint(payload: IUpdateApiEndpointPayload): Promise<IUpdateApiEndpointResponse> {
    return serviceInstances.idpService.post(API_SETTINGS_ENDPOINTS.UPDATE, payload);
  }

  bulkUpdate(payload: IBulkUpdateApiEndpointsPayload): Promise<IBulkUpdateApiEndpointsResponse> {
    return serviceInstances.idpService.post(API_SETTINGS_ENDPOINTS.BULK_UPDATE, payload);
  }

  removeEndpoints(payload: IRemoveApiEndpointsPayload): Promise<IRemoveApiEndpointsResponse> {
    return serviceInstances.idpService.post(API_SETTINGS_ENDPOINTS.REMOVE, payload);
  }
}

export const apiSettingsService = new ApiSettingsService();
