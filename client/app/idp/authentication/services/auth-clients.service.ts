/* eslint-disable @typescript-eslint/no-explicit-any */
import { serviceInstances } from "@/lib/http-client";
import { APIResponse } from "@/models/api-response";
import {
  IClientConfigResponse,
  IDeleteOidcClientPayload,
  IDeleteOidcClientResponse,
  IGetClientsPayload,
  ISaveClientCredentialPayload,
  ISaveClientCredentialResponse,
} from "@blocks-idp/authentication/models/auth.oidc.model";
import { AUTH_CLIENT_ENDPOINTS } from "../constants/endpoint.constant";

export class AuthClientsService {
  getClientCredentials(payload: IGetClientsPayload): Promise<IClientConfigResponse[]> {
    return serviceInstances.idpService.get(
      `${AUTH_CLIENT_ENDPOINTS.GET_CLIENT_CREDENTIALS}?ProjectKey=${payload.projectKey}`,
    );
  }

  saveClientCredential(
    payload: ISaveClientCredentialPayload,
  ): Promise<APIResponse<ISaveClientCredentialResponse>> {
    return serviceInstances.idpService.post(AUTH_CLIENT_ENDPOINTS.SAVE_CLIENT_CREDENTIAL, payload);
  }

  deleteClientCredential(
    payload: IDeleteOidcClientPayload,
  ): Promise<APIResponse<IDeleteOidcClientResponse>> {
    return serviceInstances.idpService.post(AUTH_CLIENT_ENDPOINTS.DELETE_CLIENT_CREDENTIAL, payload);
  }
}

export const authClientService = {
  clients: new AuthClientsService(),
};
