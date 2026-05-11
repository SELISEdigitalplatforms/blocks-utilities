import { HttpClient } from "@/lib/http-client";
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
import { deriveLogicBaseUrl } from "@/lib/blocks-url.util";
import { getRuntimeEnv } from "@/lib/runtime-env";

const logicHttp = new HttpClient(
  deriveLogicBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class OrganizationService {
  getOrganizations(params: IGetOrganizationsParams): Promise<IGetOrganizationsResponse> {
    let url = `${ORGANIZATION_ENDPOINTS.GET_ORGANIZATIONS}?projectKey=${params.projectKey}&page=${params.page}&pageSize=${params.pageSize}`;
    params.searchText ? (url += `&SearchText=${params.searchText}`) : null;
    return logicHttp.get(url);
  }

  getOrganizationById(params: IGetOrganizationByIdParams): Promise<IGetOrganizationByIdResponse> {
    return logicHttp.get(
      `${ORGANIZATION_ENDPOINTS.GET_ORGANIZATION}?ProjectKey=${params.projectKey}&ItemId=${params.itemId}`,
    );
  }

  saveOrganization = (
    payload: ICreateOrUpdateOrganizationPayload,
  ): Promise<ICreateOrUpdateOrganizationResponse> => {
    return logicHttp.post(ORGANIZATION_ENDPOINTS.SAVE_ORGANIZATION, payload);
  };

  getOrganizationConfig(projectKey: string): Promise<IOrganizationConfigResponse | null> {
    return logicHttp.get(`${ORGANIZATION_ENDPOINTS.GET_ORGANIZATION_CONFIG}?projectKey=${projectKey}`);
  }

  saveOrganizationConfig = (
    payload: IOrganizationConfigPayload,
  ): Promise<IOrganizationConfigSaveResponse> => {
    return logicHttp.post(ORGANIZATION_ENDPOINTS.SAVE_ORGANIZATION_CONFIG, payload);
  };
}

export const organizationService = new OrganizationService();
