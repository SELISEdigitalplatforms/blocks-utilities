import { serviceInstances } from "@/lib/http-client";
import {
  IDeleteSsoCredentialPayload,
  IDeleteSsoCredentialResponse,
  IGetOIDCCredentialResponse,
  IGetSsoCredentialByIdPayload,
  IGetSsoCredentialByIdResponse,
  IGetSsoCredentialsPayload,
  IGetSsoCredentialsResponse,
  ISaveSsoCredentialPayload,
  ISaveSsoCredentialResponse,
  IUpdateSsoCredentialStatusPayload,
  IUpdateSsoCredentialStatusResponse,
} from "@blocks-idp/authentication/models/sso.model";
import { SSO_ENDPOINTS, AUTH_OIDC_ENDPOINTS } from "../constants/endpoint.constant";

type SaveBlocksSsoCredentialPayload = {
  redirectUri: string;
  audience: string;
  scope: string;
  isAutoRedirect: boolean;
  itemId: string;
  projectKey: string;
};

export class SSOService {
  getSsoCredentials(payload: IGetSsoCredentialsPayload): Promise<IGetSsoCredentialsResponse> {
    return serviceInstances.idpService.get(`${SSO_ENDPOINTS.GET_SSO_CREDENTIALS}?ProjectKey=${payload.projectKey}`);
  }

  getSsoCredentialId(
    payload: IGetSsoCredentialByIdPayload,
  ): Promise<IGetSsoCredentialByIdResponse> {
    return serviceInstances.idpService.get(
      `${SSO_ENDPOINTS.GET_SSO_CREDENTIAL}?itemId=${payload.itemId}&projectKey=${payload.projectKey}`,
    );
  }

  saveSsoCredential(payload: ISaveSsoCredentialPayload): Promise<ISaveSsoCredentialResponse> {
    return serviceInstances.idpService.post(SSO_ENDPOINTS.SAVE_SSO_CREDENTIAL, payload);
  }

  deleteSsoCredential(payload: IDeleteSsoCredentialPayload): Promise<IDeleteSsoCredentialResponse> {
    return serviceInstances.idpService.post(SSO_ENDPOINTS.DELETE_SSO_CREDENTIAL, payload);
  }

  updateSsoCredentialStatus(
    payload: IUpdateSsoCredentialStatusPayload,
  ): Promise<IUpdateSsoCredentialStatusResponse> {
    return serviceInstances.idpService.post(SSO_ENDPOINTS.UPDATE_STATUS, payload);
  }

  saveBlocksSsoCredential(
    payload: SaveBlocksSsoCredentialPayload,
  ): Promise<ISaveSsoCredentialResponse> {
    return serviceInstances.idpService.post(AUTH_OIDC_ENDPOINTS.SAVE_OIDC_CLIENT, payload);
  }

  getBlocksSsoCredential(projectKey: string): Promise<IGetOIDCCredentialResponse> {
    return serviceInstances.idpService.get(`${AUTH_OIDC_ENDPOINTS.GET_OIDC_CLIENT}?ProjectKey=${projectKey}`);
  }
}

export const ssoService = new SSOService();
