import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { AuthService } from "./auth.service";
import { AUTH_ENDPOINTS } from "../constants/endpoint.constant";
import { useAuthStore } from "@/store/useAuthStore";
import { useImpersonateStore } from "@/store/impersonate-store";
import { impersonationService } from "@/services/impersonation.service";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

const env = () =>
  (window as unknown as { __BLOCKS_ENV__: Record<string, string> }).__BLOCKS_ENV__;

describe("AuthService extra methods", () => {
  let service: AuthService;

  beforeEach(() => {
    service = new AuthService();
    vi.clearAllMocks();
    useImpersonateStore.setState({ isImpersonated: false });
    useAuthStore.setState({ refreshToken: null });
    delete env().BLOCKS_IAM_BASE_URL;
    document.cookie = "impersonation_session_id=; expires=Thu, 01 Jan 1970 00:00:00 GMT";
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  describe("verifyOidc", () => {
    it("posts the authorization_code grant with basic auth headers", async () => {
      vi.mocked(http.post).mockResolvedValue({ access_token: "tok" });
      const result = await service.verifyOidc({ code: "c1", state: "s1" });

      expect(http.post).toHaveBeenCalledWith(
        AUTH_ENDPOINTS.TOKEN,
        expect.any(URLSearchParams),
        expect.objectContaining({
          "Content-Type": "application/x-www-form-urlencoded",
          Authorization: expect.stringContaining("Basic "),
        }),
        { skipTokenRotation: true },
      );
      const body = vi.mocked(http.post).mock.calls[0][1] as URLSearchParams;
      expect(body.get("grant_type")).toBe("authorization_code");
      expect(body.get("code")).toBe("c1");
      expect(body.get("state")).toBe("s1");
      expect(body.get("client_secret")).toBeTruthy();
      expect(result).toEqual({ access_token: "tok" });
    });
  });

  describe("getLoginOptions", () => {
    it("gets the login options endpoint", async () => {
      vi.mocked(http.get).mockResolvedValue({ allowedGrantTypes: ["password"] });
      const result = await service.getLoginOptions();
      expect(http.get).toHaveBeenCalledWith(AUTH_ENDPOINTS.GET_LOGIN_OPTIONS);
      expect(result).toEqual({ allowedGrantTypes: ["password"] });
    });
  });

  describe("logout", () => {
    it("uses the auth-store refresh token on localhost", async () => {
      env().BLOCKS_IAM_BASE_URL = "http://localhost:5000";
      useAuthStore.setState({ refreshToken: "local-rt" });
      vi.mocked(http.post).mockResolvedValue(undefined);

      await service.logout();

      expect(http.post).toHaveBeenCalledWith(
        AUTH_ENDPOINTS.LOGOUT,
        { refreshToken: "local-rt" },
        undefined,
        { absoluteUrl: true },
      );
    });

    it("stops impersonation and uses the session cookie when impersonated", async () => {
      useImpersonateStore.setState({ isImpersonated: true });
      document.cookie = "impersonation_session_id=sess-99";
      const stopSpy = vi
        .spyOn(impersonationService, "stopImpersonation")
        .mockResolvedValue(undefined as never);
      vi.mocked(http.post).mockResolvedValue(undefined);

      await service.logout();

      expect(stopSpy).toHaveBeenCalled();
      expect(http.post).toHaveBeenCalledWith(
        AUTH_ENDPOINTS.LOGOUT,
        { refreshToken: "sess-99" },
        undefined,
        { absoluteUrl: true },
      );
      stopSpy.mockRestore();
    });
  });
});
