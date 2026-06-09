import { http, HttpClient } from "@/lib/http-client";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useAuthStore } from "@/store/useAuthStore";
import { useImpersonateStore } from "@/store/impersonate-store";
import { impersonationService } from "@/services/impersonation.service";
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

/**
 * Gets a cookie value by name from document.cookie
 */
const getCookie = (name: string): string | null => {
  const match = document.cookie.match(new RegExp("(^| )" + name + "=([^;]+)"));
  return match ? match[2] : null;
};

const logicHttp = new HttpClient(
  deriveLogicBaseUrl(),
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export class AuthService {
  signinByEmail(
    payload: ISigninByEmailPayload,
  ): Promise<ISigninByEmailResponse> {
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
    body.append("client_secret", "***REMOVED***");

    return http.post(
      AUTH_ENDPOINTS.TOKEN,
      body,
      {
        "Content-Type": "application/x-www-form-urlencoded",
        Authorization: "Basic c2VsaXNlYmxvY2tzOkJsMDNrc0B1JFU3VjEwUw==",
      },
      {
        skipTokenRotation: true,
      },
    );
  }

  signupByEmail(
    payload: ISignupByEmailPayload,
  ): Promise<ISignupByEmailResponse> {
    return logicHttp.post(PEOPLE_ENDPOINTS.SIGNUP, payload);
  }

  getLoginOptions(): Promise<any> {
    return http.get(AUTH_ENDPOINTS.GET_LOGIN_OPTIONS);
  }

  async logout() {
    const isLocalhost = getRuntimeEnv("BLOCKS_IAM_BASE_URL").includes(
      "localhost",
    );
    const { isImpersonated } = useImpersonateStore.getState();

    let refreshToken = "";
    if (isLocalhost) {
      refreshToken = useAuthStore.getState().refreshToken || "";
    } else if (isImpersonated) {
      // Use impersonation_session_id cookie as refresh token when in impersonation mode
      refreshToken = getCookie("impersonation_session_id") || "";
    }

    // Stop impersonation first, then logout
    if (isImpersonated) {
      await impersonationService.stopImpersonation().catch(() => {});
    }

    return http.post(AUTH_ENDPOINTS.LOGOUT, { refreshToken }, undefined, {
      absoluteUrl: true,
    });
  }
}

export const authService = new AuthService();
