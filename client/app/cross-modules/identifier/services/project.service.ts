import { HttpClient } from "@/lib/http-client";
import { deriveLogicBaseUrl } from "@/lib/blocks-url.util";
import { getRuntimeEnv } from "@/lib/runtime-env";
import {
  ICreateProjectPayload,
  IDisableProjectPayload,
  IDisableProjectResponse,
  IEnvRepository,
  IGetProjectLoginOptionResponse,
  IGetProjectPayload,
  IGetProjectResponse,
  IGetPublicCertificateResponse,
  IGetSubscriptionUsageResponse,
  IMigrationInitiateResponse,
  IMigrationRequest,
  IMigrationStatusResponse,
  IMigrationVerificationResponse,
  IProjectGroup,
  IResource,
  ISavePublicCertificatePayload,
  IUpdateProjectPayload,
  IUpdateTenantGroupPayload,
  IUpdateProjectResponse,
  IValidateCNameProjectPayload,
  IValidateCNameProjectResponse,
  IVerifyMigrationRequest,
} from "@blocks-identifier/models/project.model";
import {
  GetJwtClaimPayload,
  JwtClaimPayload,
  JwtClaimResponse,
} from "@blocks-idp/authentication/models/jwt.claim.model";
import {
  PROJECT_ENDPOINTS,
  DOMAIN_ENDPOINTS,
  MIGRATION_ENDPOINTS,
  SUBSCRIPTION_ENDPOINTS,
  CLOUD_BUILD_ENDPOINTS,
} from "@blocks-identifier/constants/endpoint.constant";

const logicHttp = new HttpClient(
  deriveLogicBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class ProjectService {
  getProjects(page = 0, pageSize = 100, tenantGroupId = ""): Promise<IProjectGroup[]> {
    const url = `${PROJECT_ENDPOINTS.GETS}?page=${page}&pageSize=${pageSize}&tenantGroupId=${tenantGroupId}`;
    return logicHttp.get(url);
  }

  getAssets(tenantGroupId: string): Promise<{
    assets: {
      resources: IResource[];
      tenantGroupId: string;
      createdDate: string;
      itemId: string;
    };
    totalCount: number;
    errors: unknown | null;
    isSuccess: boolean;
  }> {
    const url = `${PROJECT_ENDPOINTS.GET_ASSET}?TenantGroupId=${tenantGroupId}`;
    return logicHttp.get(url);
  }

  addAssets(payload: { tenantGroupId: string; resource: IResource }): Promise<{
    errors: unknown | null;
    isSuccess: boolean;
  }> {
    return logicHttp.post(PROJECT_ENDPOINTS.ADD_ASSET, payload);
  }

  getEnvRepositories(projectkey: string): Promise<{
    data: IEnvRepository[];
    errors: unknown | null;
    isSuccess: boolean;
  }> {
    const url = `${CLOUD_BUILD_ENDPOINTS.REPOS_LIST}?projectkey=${projectkey}`;
    return logicHttp.get(url);
  }

  repoUpdate(payload: {
    projectKey: string;
    projectEnv: string;
    repoWithDomains: {
      repoId: string;
      repoUrl: string;
      customDeploymentDomain: string;
    }[];
  }): Promise<{
    errors: unknown | null;
    isSuccess: boolean;
  }> {
    return logicHttp.post(CLOUD_BUILD_ENDPOINTS.REPO_UPDATE, payload);
  }

  getProject(payload: IGetProjectPayload): Promise<IGetProjectResponse> {
    const url = `${PROJECT_ENDPOINTS.GET}?projectId=${payload.projectId}`;
    return logicHttp.get(url);
  }

  createProject(payload: ICreateProjectPayload): Promise<{
    isSuccess: boolean;
    errors: Record<string, string | string[]>;
    tenantGroupId: string;
  }> {
    return logicHttp.post(PROJECT_ENDPOINTS.CREATE, payload);
  }

  validateCNameProject(
    payload: IValidateCNameProjectPayload,
  ): Promise<IValidateCNameProjectResponse> {
    return logicHttp.post(DOMAIN_ENDPOINTS.CONFIGURE, payload);
  }

  updateProject(payload: IUpdateProjectPayload): Promise<IUpdateProjectResponse> {
    return logicHttp.post(PROJECT_ENDPOINTS.UPDATE, payload);
  }

  updateTenantGroup(payload: IUpdateTenantGroupPayload): Promise<IUpdateProjectResponse> {
    return logicHttp.post(PROJECT_ENDPOINTS.UPDATE_TENANT_GROUP, payload);
  }
  disableProject(payload: IDisableProjectPayload): Promise<IDisableProjectResponse> {
    return logicHttp.post(PROJECT_ENDPOINTS.DISABLE, payload);
  }

  getProjectLoginOption(): Promise<IGetProjectLoginOptionResponse> {
    return logicHttp.get(PROJECT_ENDPOINTS.GET_LOGIN_OPTIONS);
  }

  // Data Migration Methods
  initiateMigration(payload: IMigrationRequest): Promise<IMigrationInitiateResponse> {
    return logicHttp.post(MIGRATION_ENDPOINTS.MIGRATE, payload);
  }

  verifyMigration(payload: IVerifyMigrationRequest): Promise<IMigrationVerificationResponse> {
    return logicHttp.post(MIGRATION_ENDPOINTS.VERIFY, payload);
  }

  getMigrationStatus(tenantGroupId: string): Promise<IMigrationStatusResponse> {
    const url = `${MIGRATION_ENDPOINTS.GET_STATUS}?tenantGroupId=${tenantGroupId}`;
    return logicHttp.get(url);
  }

  savePublicCertificate(payload: ISavePublicCertificatePayload): Promise<IUpdateProjectResponse> {
    return logicHttp.post(PROJECT_ENDPOINTS.UPDATE_TOKEN_VALIDATION, payload);
  }

  getPublicCertificateInformation(
    projectKey: string,
  ): Promise<IGetPublicCertificateResponse | null> {
    const url = `${PROJECT_ENDPOINTS.GET_TOKEN_VALIDATION}?ProjectKey=${projectKey}`;
    return logicHttp.get<IGetPublicCertificateResponse | null>(url);
  }

  async validateJwksUrl(url: string): Promise<{
    isValid: boolean;
    error?: string;
    data?: unknown;
  }> {
    try {
      const response = await fetch(url, {
        method: "GET",
        headers: {
          "Content-Type": "application/json",
        },
      });

      if (!response.ok) {
        // invalid
        // HTTP error
        return {
          isValid: false,
          error: `Invalid, provide a valid jwks URL`,
        };
      }

      const contentType = response.headers.get("content-type");
      if (!contentType?.includes("application/json")) {
        // invalid
        // Response is not JSON
        return {
          isValid: false,
          error: "Invalid, provide a valid jwks URL",
        };
      }

      const json = await response.json();

      // Structure validation
      if (!json.keys || !Array.isArray(json.keys) || json.keys.length === 0) {
        // invalid
        // Missing or invalid 'keys' array in JWKS
        return {
          isValid: false,
          error: "Invalid, provide a valid jwks URL",
        };
      }

      return { isValid: true, data: json };
    } catch (error) {
      console.error("JWKS URL Validation Error:", error);
      return {
        isValid: false,
        error: "Invalid, provide a valid jwks URL",
      };
    }
  }

  getJwtClaim(payload: GetJwtClaimPayload): Promise<JwtClaimResponse> {
    const url = `${PROJECT_ENDPOINTS.GET_JWT_CLAIMS}?ProjectKey=${payload.projectKey}&ItemId=${payload.itemId}`;
    return logicHttp.get(url);
  }

  addJwtClaim(payload: JwtClaimPayload): Promise<{
    errors: unknown | null;
    isSuccess: boolean;
  }> {
    return logicHttp.post(PROJECT_ENDPOINTS.SAVE_JWT_CLAIMS, payload);
  }

  getSubscriptionUsage(projectKey: string): Promise<IGetSubscriptionUsageResponse> {
    return logicHttp.get(`${SUBSCRIPTION_ENDPOINTS.GETS}?projectKey=${projectKey}`);
  }
}

export const projectService = new ProjectService();
