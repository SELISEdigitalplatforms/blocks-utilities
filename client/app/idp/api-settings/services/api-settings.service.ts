import { http } from "@/lib/http-client";
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
  IApiEndpoint,
} from "../models/api-endpoint.model";

/**
 * Maps PascalCase or camelCase API response properties to camelCase frontend properties
 * Handles both response formats (old camelCase and new PascalCase from backend)
 * Computes endpoint as "controller/method"
 */
function mapApiResponseToEndpoint(data: Record<string, unknown>): IApiEndpoint {
  const getProperty = <T,>(camelCase: string, pascalCase: string, defaultValue?: T): T => {
    const camelValue = data[camelCase];
    const pascalValue = data[pascalCase];
    return (camelValue ?? pascalValue ?? defaultValue) as T;
  };

  return {
    itemId: getProperty<string>("itemId", "ItemId", ""),
    createdDate: getProperty<string>("createdDate", "CreatedDate", ""),
    lastUpdatedDate: getProperty<string>("lastUpdatedDate", "LastUpdatedDate", ""),
    createdBy: getProperty<string | null>("createdBy", "CreatedBy", null),
    lastUpdatedBy: getProperty<string>("lastUpdatedBy", "LastUpdatedBy", ""),
    language: getProperty<string | null>("language", "Language", null),
    organizationIds: getProperty<string[]>("organizationIds", "OrganizationIds", []),
    tags: getProperty<string[]>("tags", "Tags", []),
    service: getProperty<string>("service", "Service", ""),
    method: getProperty<string>("method", "Method", ""),
    description: getProperty<string>("description", "Description", ""),
    isCaptchaRequired: getProperty<boolean>("isCaptchaRequired", "IsCaptchaRequired", false),
    captchaProvider: getProperty<string>("captchaProvider", "CaptchaProvider", ""),
    isMFARequired: getProperty<boolean>("isMFARequired", "IsMFARequired", false),
    mfaType: getProperty<string>("mfaType", "MfaType", ""),
    controller: (() => {
      const ctrl = getProperty<string>("controller", "Controller", "");
      return ctrl ? ctrl.charAt(0).toUpperCase() + ctrl.slice(1) : "";
    })(),
    baseUrl: getProperty<string>("baseUrl", "BaseUrl", ""),
    version: getProperty<string>("version", "Version", ""),
  };
}

class ApiSettingsService {
  getEndpoints(payload: IGetApiEndpointsPayload): Promise<IGetApiEndpointsResponse> {
    return http.post(API_SETTINGS_ENDPOINTS.GET_LIST, {
      projectKey: payload.projectKey,
      page: payload.page ?? 0,
      pageSize: payload.pageSize ?? 100,
      filter: payload.filter ?? {},
    }).then((response: unknown) => {
      // Handle both old camelCase and new PascalCase responses from backend
      const typedResponse = response as Record<string, unknown>;
      
      // Get data array - try both naming conventions
      const data = ((typedResponse.data as Record<string, unknown>[]) || 
                    (typedResponse.Data as Record<string, unknown>[]) || 
                    []) as Record<string, unknown>[];
      
      return {
        page: ((typedResponse.page as number) || (typedResponse.Page as number)) ?? 0,
        pageSize: ((typedResponse.pageSize as number) || (typedResponse.PageSize as number)) ?? 100,
        totalPages: ((typedResponse.totalPages as number) || (typedResponse.TotalPages as number)) ?? 0,
        totalCount: ((typedResponse.totalCount as number) || (typedResponse.TotalCount as number)) ?? 0,
        data: data.map(mapApiResponseToEndpoint),
        errors: (typedResponse.errors as string[] | null) || (typedResponse.Errors as string[] | null),
      } as IGetApiEndpointsResponse;
    });
  }

  updateEndpoint(payload: IUpdateApiEndpointPayload): Promise<IUpdateApiEndpointResponse> {
    return http.post(API_SETTINGS_ENDPOINTS.UPDATE, payload);
  }

  bulkUpdate(payload: IBulkUpdateApiEndpointsPayload): Promise<IBulkUpdateApiEndpointsResponse> {
    return http.post(API_SETTINGS_ENDPOINTS.BULK_UPDATE, payload);
  }

  removeEndpoints(payload: IRemoveApiEndpointsPayload): Promise<IRemoveApiEndpointsResponse> {
    return http.post(API_SETTINGS_ENDPOINTS.REMOVE, payload);
  }
}

export const apiSettingsService = new ApiSettingsService();
