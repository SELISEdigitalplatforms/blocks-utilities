import { http } from "@/lib/http-client";
import { APIResponse } from "@/models/api-response";
import {
  IDeleteOidcClientPayload,
  IDeleteOidcClientResponse,
  IGetOidcPayload,
  IOidcConfigResponse,
  ISaveOidcCredentialPayload,
  ISaveOidcCredentialResponse,
} from "@blocks-idp/authentication/models/auth.oidc.model";
import { AUTH_OIDC_ENDPOINTS } from "../constants/endpoint.constant";

export class AuthOidc {
  async getOidcCredentials(payload: IGetOidcPayload): Promise<{
    oIDCClientCredentials: IOidcConfigResponse[];
    errors: Record<string, string> | null;
    isSuccess: boolean;
  }> {
    return http.get<{
      oIDCClientCredentials: IOidcConfigResponse[];
      errors: Record<string, string> | null;
      isSuccess: boolean;
    }>(`${AUTH_OIDC_ENDPOINTS.GET_OIDC_CLIENTS}?ProjectKey=${payload.projectKey}`);
  }

  async getOidcCredential(payload: IGetOidcPayload): Promise<{
    oIDCClientCredential: IOidcConfigResponse;
    errors: Record<string, string> | null;
    isSuccess: boolean;
  }> {
    return http.get<{
      oIDCClientCredential: IOidcConfigResponse;
      errors: Record<string, string> | null;
      isSuccess: boolean;
    }>(
      `${AUTH_OIDC_ENDPOINTS.GET_OIDC_CLIENT}?ProjectKey=${payload.projectKey}&ClientId=${payload.clientId}`,
    );
  }

  saveOidcCredential(
    payload: ISaveOidcCredentialPayload,
  ): Promise<APIResponse<ISaveOidcCredentialResponse>> {
    return http.post(AUTH_OIDC_ENDPOINTS.SAVE_OIDC_CLIENT, payload);
  }

  deleteOidcCredential(
    payload: IDeleteOidcClientPayload,
  ): Promise<APIResponse<IDeleteOidcClientResponse>> {
    return http.post(AUTH_OIDC_ENDPOINTS.DELETE_OIDC_CLIENT, payload);
  }
}

export const authOidc = {
  clients: new AuthOidc(),
};
