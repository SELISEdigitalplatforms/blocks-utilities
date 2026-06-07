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
import { deriveIdpBaseUrl } from "@/lib/blocks-url.util";
import { getRuntimeEnv } from "@/lib/runtime-env";

const iamHttp = new HttpClient(
  deriveIdpBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class OrganizationService {
  getOrganizations(params: IGetOrganizationsParams): Promise<IGetOrganizationsResponse> {
    let url = `${ORGANIZATION_ENDPOINTS.GET_ORGANIZATIONS}?projectKey=${params.projectKey}&page=${params.page}&pageSize=${params.pageSize}`;
    params.searchText ? (url += `&SearchText=${params.searchText}`) : null;
    return iamHttp.get(url, undefined, { absoluteUrl: true });
  }

  getOrganizationById(params: IGetOrganizationByIdParams): Promise<IGetOrganizationByIdResponse> {
    return iamHttp.get(
      `${ORGANIZATION_ENDPOINTS.GET_ORGANIZATION}?ProjectKey=${params.projectKey}&ItemId=${params.itemId}`,
      undefined,
      { absoluteUrl: true },
    );
  }

  saveOrganization = (
    payload: ICreateOrUpdateOrganizationPayload,
  ): Promise<ICreateOrUpdateOrganizationResponse> => {
    return iamHttp.post(ORGANIZATION_ENDPOINTS.SAVE_ORGANIZATION, payload, undefined, { absoluteUrl: true });
  };

  getOrganizationConfig(projectKey: string): Promise<IOrganizationConfigResponse | null> {
    return iamHttp.get(`${ORGANIZATION_ENDPOINTS.GET_ORGANIZATION_CONFIG}?projectKey=${projectKey}`, undefined, { absoluteUrl: true });
  }

  saveOrganizationConfig = (
    payload: IOrganizationConfigPayload,
  ): Promise<IOrganizationConfigSaveResponse> => {
    return iamHttp.post(ORGANIZATION_ENDPOINTS.SAVE_ORGANIZATION_CONFIG, payload, undefined, { absoluteUrl: true });
  };
}

export const organizationService = new OrganizationService();
