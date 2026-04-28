import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { MFAService } from "./mfa.service";
import { MFA_CONFIG_ENDPOINTS, MFA_ENDPOINTS } from "../constants/endpoint.constant";
import {
  mockGetMfaConfigPayload,
  mockMfaConfigResponse,
  mockSaveMfaConfigPayload,
  mockGenerateOtpPayload,
  mockGenerateOtpResponse,
  mockConfigureUserMfaPayload,
  mockSetupTotpPayload,
  mockSetupTotpResponse,
  mockVerifyOtpPayload,
  mockVerifyOtpResponse,
  mockResendOtpPayload,
  mockDisableMfaPayload,
  mockSuccessResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("MFAService", () => {
  let service: MFAService;

  beforeEach(() => {
    service = new MFAService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getConfigurations ────────────────────────────────────────────────────
  describe("getConfigurations", () => {
    it("should GET with correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockMfaConfigResponse);

      const result = await service.getConfigurations(mockGetMfaConfigPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${MFA_CONFIG_ENDPOINTS.GET}?ProjectKey=${mockGetMfaConfigPayload.projectKey}`,
      );
      expect(result).toEqual(mockMfaConfigResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getConfigurations(mockGetMfaConfigPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── saveMFAConfiguration ─────────────────────────────────────────────────
  describe("saveMFAConfiguration", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.saveMFAConfiguration(mockSaveMfaConfigPayload);

      expect(http.post).toHaveBeenCalledWith(MFA_CONFIG_ENDPOINTS.SAVE, mockSaveMfaConfigPayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.saveMFAConfiguration(mockSaveMfaConfigPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── generateUserMfaOTP ───────────────────────────────────────────────────
  describe("generateUserMfaOTP", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockGenerateOtpResponse);

      const result = await service.generateUserMfaOTP(mockGenerateOtpPayload);

      expect(http.post).toHaveBeenCalledWith(MFA_ENDPOINTS.GENERATE_OTP, mockGenerateOtpPayload);
      expect(result).toEqual(mockGenerateOtpResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.generateUserMfaOTP(mockGenerateOtpPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── configureUserMFA ─────────────────────────────────────────────────────
  describe("configureUserMFA", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.configureUserMFA(mockConfigureUserMfaPayload);

      expect(http.post).toHaveBeenCalledWith(
        MFA_ENDPOINTS.CONFIGURE_USER_MFA,
        mockConfigureUserMfaPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.configureUserMFA(mockConfigureUserMfaPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── setupUserTotp ────────────────────────────────────────────────────────
  describe("setupUserTotp", () => {
    it("should GET with correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockSetupTotpResponse);

      const result = await service.setupUserTotp(mockSetupTotpPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${MFA_ENDPOINTS.SETUP_TOTP}?UserId=${mockSetupTotpPayload.id}&ProjectKey=${mockSetupTotpPayload.projectKey}`,
      );
      expect(result).toEqual(mockSetupTotpResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.setupUserTotp(mockSetupTotpPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── verifyOtp ────────────────────────────────────────────────────────────
  describe("verifyOtp", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockVerifyOtpResponse);

      const result = await service.verifyOtp(mockVerifyOtpPayload);

      expect(http.post).toHaveBeenCalledWith(MFA_ENDPOINTS.VERIFY_OTP, mockVerifyOtpPayload);
      expect(result).toEqual(mockVerifyOtpResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.verifyOtp(mockVerifyOtpPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── resendOtp ────────────────────────────────────────────────────────────
  describe("resendOtp", () => {
    it("should POST the mfaId to the correct endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.resendOtp(mockResendOtpPayload);

      expect(http.post).toHaveBeenCalledWith(MFA_ENDPOINTS.RESEND_OTP, mockResendOtpPayload.mfaId);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.resendOtp(mockResendOtpPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── disableMFA ───────────────────────────────────────────────────────────
  describe("disableMFA", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.disableMFA(mockDisableMfaPayload);

      expect(http.post).toHaveBeenCalledWith(MFA_ENDPOINTS.DISABLE_MFA, mockDisableMfaPayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.disableMFA(mockDisableMfaPayload)).rejects.toThrow("Network error");
    });
  });
});
