import { GRANT_TYPES, SSO_PROVIDERS } from "@blocks-idp/authentication/constants";

export interface IProject {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  name: string;
  applicationDomain: string;
  customDomain: string;
  isProduction: true;
  tenantId: string;
  isCookieEnable: boolean;
  isDomainVerified: boolean;
  cookieDomain: string;
  isDisabled: boolean;
  environment: string;
  tenantGroupId: string;
  tenantSlug: string;
}

export interface IResource {
  name: string;
  link: string;
  resourceId: string;
}
export interface IProjectGroup {
  tenantGroupId: string;
  projects: IProject[];
}
export interface ICreateProjectPayload {
  name: string;
  isAcceptBlocksTerms: boolean;
  isUseBlocksExclusively: boolean;
  isProduction: boolean;
  resources: IResource[];
  applicationContexts: {
    environment: string;
    domain: string;
    cookieDomain: string;
  }[];
  tenantGroupId?: string;
}
export interface IGetProjectPayload {
  projectId: string;
}
export interface IGetProjectResponse {
  data: IProject;
  errors: unknown | null;
}

export interface IEnvRepository {
  itemId: string;
  repoName: string;
  repoUrl: string;
  defaultDeploymentUrl: string;
  customDeploymentUrl: string;
  lastDeploymentDate: string;
}

export interface IGetProjectAuthConfig {
  accountLockDurationInMinutes: number;
  certificateValidForNumberOfDays: number;
  getNumberOfWrongAttemptsToLockTheAccount: number;
  publicCertificatePath: string;
  certificateIssueDate: string;
  refreshTokenValidForNumberMinutes: number;
  allowedGrantTypes: string[];
}
export interface IGetProjectAuthConfigPayload {
  projectId: string;
}
export interface IGetProjectAuthConfigResponse extends IGetProjectAuthConfig {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface ISaveProjectAuthConfigPayload {
  refreshTokenValidForNumberMinutes: number;
  getNumberOfWrongAttemptsToLockTheAccount: number;
  accountLockDurationInMinutes: number;
  projectId: string;
  allowedGrantTypes: string[];
}
export interface ISaveProjectAuthConfigResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface ISavePublicCertificatePayload {
  projectKey: string;
  publicCertificatePassword: string;
  issuer: string;
  audiences: string[];
  publicCertificatePath: string;
  jwksUrl: string;
  providerName: string;
  cookieKey?: string;
}

export interface IValidateCNameProjectPayload {
  projectKey: string;
  cookieDomain: string;
}
export interface IValidateCNameProjectResponse {
  errors: unknown | null;
  isSuccess: boolean;
  isStatusChanged: boolean;
}

export interface IUpdateProjectPayload {
  name: string;
  applicationDomain: string;
  isCookieEnable?: boolean;
  cookieDomain?: string;
  useCustomDomain: boolean;
  customDomain: string;
  projectKey: string;
}
export interface IUpdateTenantGroupPayload {
  name: string;
  tenantGroupId: string;
}
export interface IUpdateProjectResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface IDisableProjectPayload {
  projectKey: string;
}
export interface IDisableProjectResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
type SSO_INFO = {
  provider: SSO_PROVIDERS;
  audience: string;
};

export type LoginOption = {
  allowedGrantTypes: GRANT_TYPES[];
  ssoInfo: SSO_INFO[];
};
export type IGetProjectLoginOptionResponse = LoginOption;

// Data Migration interfaces
export interface IMigrationServiceDetails {
  shouldOverWriteExistingData: boolean;
  serviceName: number;
}

export interface IMigrationRequest {
  projectKey: string;
  targetedProjectKey: string;
  tenantGroupId: string;
  services: IMigrationServiceDetails[];
}

export interface IMigrationInitiateResponse {
  verificationId: string;
  isSuccess: boolean;
}

export interface IVerifyMigrationRequest {
  verificationId: string;
  verificationCode: string;
}

export interface IMigrationVerificationResponse {
  isValid: boolean;
  isSuccess: boolean;
  errors: unknown | null;
}

export type IMigrationStatusResponse = Array<{
  targetedProjectKey: string;
}>;

export interface IGetPublicCertificateResponse {
  issuer: string;
  audiences: string[];
  publicCertificatePath: string;
  jwksUrl: string;
  cookieKey: string | null;
  isConfigured: boolean;
  providerName: string | null;
}

export interface ISubscription {
  resource: string;
  resourceType: string | null;
  limit: number;
  usage: number;
  lifetime: string;
  isActive: boolean;
  enableAutoRenew: boolean;
  tenantId: string;
  type: string;
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
}

export interface IGetSubscriptionUsageResponse {
  subscriptions: ISubscription[];
  errors: unknown | null;
  isSuccess: boolean;
}
