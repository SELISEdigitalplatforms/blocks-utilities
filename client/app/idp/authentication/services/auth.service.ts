import { http, HttpClient } from "@/lib/http-client";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useAuthStore } from "@/store/useAuthStore";
import {
  ISigninByEmailPayload,
  ISigninByEmailResponse,
  ISignupByEmailPayload,
  ISignupByEmailResponse,
  IVerifyMfaPayload,
  IVerifyMfaResponse,
} from "@blocks-idp/authentication/models/auth.model";
import { AUTH_ENDPOINTS } from "../constants/endpoint.constant";
import { PEOPLE_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";
import { deriveLogicBaseUrl } from "@/lib/blocks-url.util";

const logicHttp = new HttpClient(
  deriveLogicBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class AuthService {
  signinByEmail(payload: ISigninByEmailPayload): Promise<ISigninByEmailResponse> {
    const body = new URLSearchParams();
    body.append("grant_type", "password");
    body.append("username", payload.username);
    body.append("password", payload.password);

    return http.post(
      AUTH_ENDPOINTS.TOKEN,
      body,
      {
        "Content-Type": "application/x-www-form-urlencoded",
      },
      {
        skipTokenRotation: true,
      },
    );
  }

  verifyMfa(payload: IVerifyMfaPayload): Promise<IVerifyMfaResponse> {
    const body = new URLSearchParams();
    body.append("grant_type", "mfa_code");
    body.append("code", payload.code);
    body.append("mfa_id", payload.mfa_id);
    body.append("mfa_type", payload.mfa_type.toString());
    return http.post(AUTH_ENDPOINTS.TOKEN, body, {
      "Content-Type": "application/x-www-form-urlencoded",
    });
  }

  verifyOidc(payload: { code: string; state: string }): Promise<any> {
    const body = new URLSearchParams();
    body.append("grant_type", "authorization_code");
    body.append("code", payload.code);
    body.append("state", payload.state);
    body.append("client_secret", "4a106add28be4db4bd59ed8400c3693a");

    return http.post(
      AUTH_ENDPOINTS.TOKEN,
      body,
      {
        "Content-Type": "application/x-www-form-urlencoded",
        "Authorization": "Basic c2VsaXNlYmxvY2tzOkJsMDNrc0B1JFU3VjEwUw=="
      },
      {
        skipTokenRotation: true,
      },
    );
  }

  signupByEmail(payload: ISignupByEmailPayload): Promise<ISignupByEmailResponse> {
    return logicHttp.post(PEOPLE_ENDPOINTS.SIGNUP, payload);
  }

  getLoginOptions(): Promise<any> {
    return http.get(AUTH_ENDPOINTS.GET_LOGIN_OPTIONS);
  }

  logout() {
    const isLocalhost = getRuntimeEnv("BLOCKS_IDP_BASE_URL").includes("localhost");
    const refreshToken = isLocalhost ? (useAuthStore.getState().refreshToken || "") : "";
    return http.post(AUTH_ENDPOINTS.LOGOUT, { refreshToken }, undefined, {
      absoluteUrl: true,
    });
  }
}

export const authService = new AuthService();
