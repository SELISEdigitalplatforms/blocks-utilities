import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { AuthService } from "./auth.service";
import { AUTH_ENDPOINTS } from "../constants/endpoint.constant";
import { PEOPLE_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";
import {
  mockSigninPayload,
  mockSigninResponse,
  mockSignupPayload,
  mockSignupResponse,
  mockVerifyMfaPayload,
  mockVerifyMfaResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("AuthService", () => {
  let service: AuthService;

  beforeEach(() => {
    service = new AuthService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── signinByEmail ──────────────────────────────────────────────────────────
  describe("signinByEmail", () => {
    it("should POST form-encoded credentials to TOKEN endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSigninResponse);

      const result = await service.signinByEmail(mockSigninPayload);

      expect(http.post).toHaveBeenCalledWith(
        AUTH_ENDPOINTS.TOKEN,
        expect.any(URLSearchParams),
        { "Content-Type": "application/x-www-form-urlencoded" },
        { skipTokenRotation: true },
      );

      const body = vi.mocked(http.post).mock.calls[0][1] as URLSearchParams;
      expect(body.get("grant_type")).toBe("password");
      expect(body.get("username")).toBe(mockSigninPayload.username);
      expect(body.get("password")).toBe(mockSigninPayload.password);
      expect(result).toEqual(mockSigninResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.signinByEmail(mockSigninPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── verifyMfa ──────────────────────────────────────────────────────────────
  describe("verifyMfa", () => {
    it("should POST form-encoded MFA payload to TOKEN endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockVerifyMfaResponse);

      const result = await service.verifyMfa(mockVerifyMfaPayload);

      expect(http.post).toHaveBeenCalledWith(AUTH_ENDPOINTS.TOKEN, expect.any(URLSearchParams), {
        "Content-Type": "application/x-www-form-urlencoded",
      });

      const body = vi.mocked(http.post).mock.calls[0][1] as URLSearchParams;
      expect(body.get("grant_type")).toBe("mfa_code");
      expect(body.get("code")).toBe(mockVerifyMfaPayload.code);
      expect(body.get("mfa_id")).toBe(mockVerifyMfaPayload.mfa_id);
      expect(body.get("mfa_type")).toBe(mockVerifyMfaPayload.mfa_type.toString());
      expect(result).toEqual(mockVerifyMfaResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.verifyMfa(mockVerifyMfaPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── signupByEmail ──────────────────────────────────────────────────────────
  describe("signupByEmail", () => {
    it("should POST to the SIGNUP endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSignupResponse);

      const result = await service.signupByEmail(mockSignupPayload);

      expect(http.post).toHaveBeenCalledWith(PEOPLE_ENDPOINTS.SIGNUP, mockSignupPayload);
      expect(result).toEqual(mockSignupResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.signupByEmail(mockSignupPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── logout ─────────────────────────────────────────────────────────────────
  describe("logout", () => {
    it("should POST to the LOGOUT endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(undefined);

      await service.logout();

      expect(http.post).toHaveBeenCalledWith(AUTH_ENDPOINTS.LOGOUT, { refreshToken: "" });
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.logout()).rejects.toThrow("Network error");
    });
  });
});
