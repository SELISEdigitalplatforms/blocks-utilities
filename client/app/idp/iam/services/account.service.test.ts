import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { UserAccountService } from "./account.service";
import { ACCOUNT_ENDPOINTS } from "../constants/endpoint.constant";
import {
  mockAccountActivationPayload,
  mockResendActivationPayload,
  mockAccountRecoverPayload,
  mockAccountResetPasswordPayload,
  mockActivationCodeValidationPayload,
  mockActivationCodeExpirationResponse,
  mockSuccessResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("UserAccountService", () => {
  let service: UserAccountService;

  beforeEach(() => {
    service = new UserAccountService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── accountActivation ────────────────────────────────────────────────────
  describe("accountActivation", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.accountActivation(mockAccountActivationPayload);

      expect(http.post).toHaveBeenCalledWith(
        ACCOUNT_ENDPOINTS.ACTIVATE,
        mockAccountActivationPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.accountActivation(mockAccountActivationPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── accountResendActivation ──────────────────────────────────────────────
  describe("accountResendActivation", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.accountResendActivation(mockResendActivationPayload);

      expect(http.post).toHaveBeenCalledWith(
        ACCOUNT_ENDPOINTS.RESEND_ACTIVATION,
        mockResendActivationPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.accountResendActivation(mockResendActivationPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── accountRecover ───────────────────────────────────────────────────────
  describe("accountRecover", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.accountRecover(mockAccountRecoverPayload);

      expect(http.post).toHaveBeenCalledWith(ACCOUNT_ENDPOINTS.RECOVER, mockAccountRecoverPayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.accountRecover(mockAccountRecoverPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── accountResetPassword ─────────────────────────────────────────────────
  describe("accountResetPassword", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.accountResetPassword(mockAccountResetPasswordPayload);

      expect(http.post).toHaveBeenCalledWith(
        ACCOUNT_ENDPOINTS.RESET_PASSWORD,
        mockAccountResetPasswordPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.accountResetPassword(mockAccountResetPasswordPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── checkActivationCodeExpiration ────────────────────────────────────────
  describe("checkActivationCodeExpiration", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockActivationCodeExpirationResponse);

      const result = await service.checkActivationCodeExpiration(
        mockActivationCodeValidationPayload,
      );

      expect(http.post).toHaveBeenCalledWith(
        ACCOUNT_ENDPOINTS.VALIDATE_ACTIVATION_CODE,
        mockActivationCodeValidationPayload,
      );
      expect(result).toEqual(mockActivationCodeExpirationResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(
        service.checkActivationCodeExpiration(mockActivationCodeValidationPayload),
      ).rejects.toThrow("Network error");
    });
  });
});
