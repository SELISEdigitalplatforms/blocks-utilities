import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { ConfigurationService } from "./configuration.service";
import { IAM_CONFIGURATION_ENDPOINTS } from "../constants/endpoint.constant";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";
import {
  mockGetIamConfigResponse,
  mockSaveIamConfigPayload,
  mockSuccessResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("ConfigurationService", () => {
  let service: ConfigurationService;

  beforeEach(() => {
    service = new ConfigurationService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getIamConfiguration ──────────────────────────────────────────────────
  describe("getIamConfiguration", () => {
    it("should GET with correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetIamConfigResponse);

      const result = await service.getIamConfiguration(TEST_PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${IAM_CONFIGURATION_ENDPOINTS.GET}?ProjectKey=${TEST_PROJECT_KEY}`,
      );
      expect(result).toEqual(mockGetIamConfigResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getIamConfiguration(TEST_PROJECT_KEY)).rejects.toThrow("Network error");
    });
  });

  // ─── saveIamConfiguration ─────────────────────────────────────────────────
  describe("saveIamConfiguration", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.saveIamConfiguration(mockSaveIamConfigPayload);

      expect(http.post).toHaveBeenCalledWith(IAM_CONFIGURATION_ENDPOINTS.SAVE, {
        ...mockSaveIamConfigPayload,
      });
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.saveIamConfiguration(mockSaveIamConfigPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });
});
