import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";
import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";

export interface IAuthConfiguration {
  accessTokenValidForNumberMinutes: number;
  accountLockDurationInMinutes: number;
  allowedGrantTypes: string[];
  getNumberOfWrongAttemptsToLockTheAccount: number;
  itemId: string;
  refreshTokenValidForNumberMinutes: number;
  rememberMeRefreshTokenValidForNumberMinutes: number;
  publicCertificatePath: string;
  isSelfSignUpAllowed: boolean;
}

export interface IAuthConfigPayload {
  projectKey: string;
}

export interface IGetAuthConfigResponse extends IAuthConfiguration {
  errors: unknown | null;
  isSuccess: boolean;
}

export interface ISaveAuthConfigPayload {
  itemId: string;
  refreshTokenValidForNumberMinutes: number;
  getNumberOfWrongAttemptsToLockTheAccount: number;
  accountLockDurationInMinutes: number;
  accessTokenValidForNumberMinutes: number;
  rememberMeRefreshTokenValidForNumberMinutes: number;
  allowedGrantTypes: string[];
  projectKey: string;
  isSelfSignUpAllowed: boolean;
}

export interface ISaveAuthConfigResponse {
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
