import { serviceInstances } from "@/lib/http-client";
import {
  ICreateOrUpdateOrganizationPayload,
  ICreateOrUpdateOrganizationResponse,
  IGetOrganizationByIdParams,
  IGetOrganizationByIdResponse,
  IGetOrganizationsParams,
  IGetOrganizationsResponse,
} from "@blocks-idp/iam/models/organization";
import {
  IOrganizationConfigPayload,
  IOrganizationConfigResponse,
  IOrganizationConfigSaveResponse,
} from "@blocks-idp/iam/models/organization-config.model";
import { ORGANIZATION_ENDPOINTS } from "../constants/endpoint.constant";

export class OrganizationService {
  getOrganizations(params: IGetOrganizationsParams): Promise<IGetOrganizationsResponse> {
    const query = new URLSearchParams({
      projectKey: params.projectKey,
      page: String(params.page),
      pageSize: String(params.pageSize),
    });

    // IAM binds the filter as a nested object, so the search term is `filter.search`
    // rather than a flat `SearchText`.
    if (params.searchText) {
      query.set("filter.search", params.searchText);
    }

    return serviceInstances.idpService.get(
      `${ORGANIZATION_ENDPOINTS.GET_ORGANIZATIONS}?${query.toString()}`,
      undefined,
      { absoluteUrl: true },
    );
  }

  getOrganizationById(params: IGetOrganizationByIdParams): Promise<IGetOrganizationByIdResponse> {
    return serviceInstances.idpService.get(
      `${ORGANIZATION_ENDPOINTS.GET_ORGANIZATION}/${encodeURIComponent(params.itemId)}`,
      undefined,
      { absoluteUrl: true },
    );
  }

  saveOrganization = (
    payload: ICreateOrUpdateOrganizationPayload,
  ): Promise<ICreateOrUpdateOrganizationResponse> => {
    // Create and update are different routes at IAM: an existing organization is addressed
    // by id, a new one goes to `create`.
    const url = payload.itemId
      ? `${ORGANIZATION_ENDPOINTS.SAVE_ORGANIZATION}/${encodeURIComponent(payload.itemId)}`
      : `${ORGANIZATION_ENDPOINTS.SAVE_ORGANIZATION}/create`;

    return serviceInstances.idpService.post(url, payload, undefined, { absoluteUrl: true });
  };

  getOrganizationConfig(projectKey: string): Promise<IOrganizationConfigResponse | null> {
    return serviceInstances.idpService.get(`${ORGANIZATION_ENDPOINTS.GET_ORGANIZATION_CONFIG}?projectKey=${projectKey}`, undefined, { absoluteUrl: true });
  }

  saveOrganizationConfig = (
    payload: IOrganizationConfigPayload,
  ): Promise<IOrganizationConfigSaveResponse> => {
    return serviceInstances.idpService.post(ORGANIZATION_ENDPOINTS.SAVE_ORGANIZATION_CONFIG, payload, undefined, { absoluteUrl: true });
  };
}

export const organizationService = new OrganizationService();
