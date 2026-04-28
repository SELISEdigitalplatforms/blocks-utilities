import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";

export interface IGetSocialLoginEndpointPayload {
  provider: SSO_PROVIDERS;
  audience: string;
  nextUrl?: string;
  sendAsResponse: boolean;
}
export interface IGetSocialLoginEndpointResponse {
  error: unknown;
  isAResponse: boolean;
  providerUrl: string;
}
export interface ISigninBySSOPayload {
  code: string;
  state: string;
}
export interface ISigninBySSOResponse {
  access_token: string;
  expires_in: number;
  refresh_token: string;
  token_type: string;
  enable_mfa: boolean;
  message: string;
  mfaId: string;
  mfaType: string;
  sso_user_redirect_url?: string;
}
