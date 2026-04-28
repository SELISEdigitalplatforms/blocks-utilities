import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { IRole } from "@blocks-idp/iam/models/role";

export interface ISsoProviderConfiguration {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  provider: SSO_PROVIDERS;
  audience: string;
  clientId: string;
  clientSecret: string;
  authorizationUrl: string;
  tokenUrl: string;
  getProfileUrl: string;
  redirectUrl: string;
  scope: string[];
  initialRoles: string[];
  initialPermissions: string[];
  isDisabled: boolean;
  userRoles: IRole[];
  userPermissions: IPermission[];
  isAutoRedirect?: boolean;
  wellKnownUrl?: string;
}

export interface ISsoProviderFrontendMeta {
  label: string;
  description: string;
  isConfigured?: boolean;
  imageSrc: string;
  imageSrcDark?: string;
  isAvailable?: boolean;
}

export type ISsoProviderConfigurationWithMeta = ISsoProviderConfiguration &
  ISsoProviderFrontendMeta;

export interface ISaveSsoCredentialPayload {
  itemId?: string;
  provider: string;
  audience: string;
  clientId: string;
  clientSecret: string;
  redirectUrl: string;
  projectKey: string;
  initialRoles: string[];
  initialPermissions: string[];
}

export interface ISaveSsoCredentialResponse {
  isSuccess: boolean;
  errors: unknown;
  itemId: string;
}

export interface IDeleteSsoCredentialPayload {
  itemId: string;
  projectKey: string;
}

export interface IDeleteSsoCredentialResponse {
  isSuccess: boolean;
  errors: unknown;
}
export interface IGetSsoCredentialByIdPayload {
  itemId: string;
  projectKey: string;
}

export interface IGetSsoCredentialByIdResponse extends ISsoProviderConfiguration {}
export interface IGetSsoCredentialsPayload {
  projectKey: string;
}

export type IGetSsoCredentialsResponse = ISsoProviderConfiguration[];

export interface IUpdateSsoCredentialStatusPayload {
  itemId: string;
  isEnabled: boolean;
  projectKey: string;
}

export interface IUpdateSsoCredentialStatusResponse {
  isSuccess: boolean;
  errors: unknown;
}

export interface IGetOIDCCredentialResponse {
  audience?: string;
  itemId?: string;
  clientId?: string;
  clientSecret?: string;
  redirectUri?: string;
  isAutoRedirect?: boolean;
  scope?: string;
}
