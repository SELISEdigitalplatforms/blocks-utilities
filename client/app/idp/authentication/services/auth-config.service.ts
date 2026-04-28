import { http } from "@/lib/http-client";
import {
  IAuthConfigPayload,
  IGetAuthConfigResponse,
  ISaveAuthConfigPayload,
  ISaveAuthConfigResponse,
} from "@blocks-idp/authentication/models/auth-configuration.model";
import { AUTH_CONFIG_ENDPOINTS } from "../constants/endpoint.constant";

export class AuthConfiguration {
  getConfig(payload: IAuthConfigPayload): Promise<IGetAuthConfigResponse> {
    const url = `${AUTH_CONFIG_ENDPOINTS.GET_CONFIG}?ProjectKey=${payload.projectKey}`;
    return http.get(url);
  }

  saveAuthConfig(payload: ISaveAuthConfigPayload): Promise<ISaveAuthConfigResponse> {
    return http.post(AUTH_CONFIG_ENDPOINTS.UPDATE_CONFIG, payload);
  }
}
