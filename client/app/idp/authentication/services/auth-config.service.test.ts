import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { AuthConfiguration } from "./auth-config.service";
import { AUTH_CONFIG_ENDPOINTS } from "../constants/endpoint.constant";
import {
  mockGetAuthConfigPayload,
  mockGetAuthConfigResponse,
  mockSaveAuthConfigPayload,
  mockSuccessResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("AuthConfiguration", () => {
  let service: AuthConfiguration;

  beforeEach(() => {
    service = new AuthConfiguration();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getConfig ────────────────────────────────────────────────────────────
  describe("getConfig", () => {
    it("should GET with correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetAuthConfigResponse);

      const result = await service.getConfig(mockGetAuthConfigPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${AUTH_CONFIG_ENDPOINTS.GET_CONFIG}?ProjectKey=${mockGetAuthConfigPayload.projectKey}`,
      );
      expect(result).toEqual(mockGetAuthConfigResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getConfig(mockGetAuthConfigPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── saveAuthConfig ───────────────────────────────────────────────────────
  describe("saveAuthConfig", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.saveAuthConfig(mockSaveAuthConfigPayload);

      expect(http.post).toHaveBeenCalledWith(
        AUTH_CONFIG_ENDPOINTS.UPDATE_CONFIG,
        mockSaveAuthConfigPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.saveAuthConfig(mockSaveAuthConfigPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });
});
