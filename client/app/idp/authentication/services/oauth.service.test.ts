import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  mockGetSocialLoginPayload,
  mockGetSocialLoginResponse,
  mockSigninBySSOPayload,
  mockSigninBySSOResponse,
} from "../../test-utils/__mocks__";
import { AUTH_ENDPOINTS } from "../constants/endpoint.constant";
import { OAuthService } from "./oauth.service";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("OAuthService", () => {
  let service: OAuthService;

  beforeEach(() => {
    service = new OAuthService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getSocialLoginEndpoint ───────────────────────────────────────────────
  describe("getSocialLoginEndpoint", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockGetSocialLoginResponse);

      const result = await service.getSocialLoginEndpoint(mockGetSocialLoginPayload);

      expect(http.post).toHaveBeenCalledWith(
        AUTH_ENDPOINTS.GET_SOCIAL_LOGIN_ENDPOINT,
        mockGetSocialLoginPayload,
      );
      expect(result).toEqual(mockGetSocialLoginResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.getSocialLoginEndpoint(mockGetSocialLoginPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── signinBySSO ──────────────────────────────────────────────────────────
  describe("signinBySSO", () => {
    it("should POST form-encoded SSO data to TOKEN endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSigninBySSOResponse);

      const result = await service.signinBySSO(mockSigninBySSOPayload);

      expect(http.post).toHaveBeenCalledWith(
        AUTH_ENDPOINTS.TOKEN,
        expect.any(URLSearchParams),
        {
          "Content-Type": "application/x-www-form-urlencoded",
        },
        {
          skipTokenRotation: true,
        },
      );

      const body = vi.mocked(http.post).mock.calls[0][1] as URLSearchParams;
      expect(body.get("grant_type")).toBe("social");
      expect(body.get("code")).toBe(mockSigninBySSOPayload.code);
      expect(body.get("state")).toBe(mockSigninBySSOPayload.state);
      expect(result).toEqual(mockSigninBySSOResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.signinBySSO(mockSigninBySSOPayload)).rejects.toThrow("Network error");
    });
  });
});
