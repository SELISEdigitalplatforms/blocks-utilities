import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { CaptchaService } from "./captcha.service";
import { CAPTCHA_ENDPOINTS } from "../constants/endpoint.constant";
import {
  mockGetCaptchaConfigsPayload,
  mockCaptchaConfigsResponse,
  mockSaveCaptchaPayload,
  mockUpdateCaptchaStatusPayload,
  mockSuccessResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("CaptchaService", () => {
  let service: CaptchaService;

  beforeEach(() => {
    service = new CaptchaService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getCaptchaConfigs ────────────────────────────────────────────────────
  describe("getCaptchaConfigs", () => {
    it("should GET with correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockCaptchaConfigsResponse);

      const result = await service.getCaptchaConfigs(mockGetCaptchaConfigsPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${CAPTCHA_ENDPOINTS.GETS}?ProjectKey=${mockGetCaptchaConfigsPayload.projectKey}`,
      );
      expect(result).toEqual(mockCaptchaConfigsResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getCaptchaConfigs(mockGetCaptchaConfigsPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── saveCaptcha ──────────────────────────────────────────────────────────
  describe("saveCaptcha", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.saveCaptcha(mockSaveCaptchaPayload);

      expect(http.post).toHaveBeenCalledWith(CAPTCHA_ENDPOINTS.SAVE, mockSaveCaptchaPayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.saveCaptcha(mockSaveCaptchaPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── updateCaptchaConfigStatus ────────────────────────────────────────────
  describe("updateCaptchaConfigStatus", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.updateCaptchaConfigStatus(mockUpdateCaptchaStatusPayload);

      expect(http.post).toHaveBeenCalledWith(
        CAPTCHA_ENDPOINTS.UPDATE_STATUS,
        mockUpdateCaptchaStatusPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(
        service.updateCaptchaConfigStatus(mockUpdateCaptchaStatusPayload),
      ).rejects.toThrow("Network error");
    });
  });
});
