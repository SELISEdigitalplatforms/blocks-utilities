import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { UsageService } from "./usage.service";
import { TRACE_ENDPOINTS } from "../constants/endpoint.constant";
import {
  mockUsageMatrixResponse,
  mockGetOperationalAnalyticsPayload,
  mockGetServiceAnalyticsPayload,
} from "../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("UsageService", () => {
  let service: UsageService;

  beforeEach(() => {
    service = new UsageService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getOperationalAnalytics ──────────────────────────────────────────────
  describe("getOperationalAnalytics", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockUsageMatrixResponse);

      const result = await service.getOperationalAnalytics(mockGetOperationalAnalyticsPayload);

      expect(http.post).toHaveBeenCalledWith(
        TRACE_ENDPOINTS.GET_OPERATIONAL_ANALYTICS,
        mockGetOperationalAnalyticsPayload,
      );
      expect(result).toEqual(mockUsageMatrixResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(
        service.getOperationalAnalytics(mockGetOperationalAnalyticsPayload),
      ).rejects.toThrow("Network error");
    });
  });

  // ─── getServiceAnalytics ──────────────────────────────────────────────────
  describe("getServiceAnalytics", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockUsageMatrixResponse);

      const result = await service.getServiceAnalytics(mockGetServiceAnalyticsPayload);

      expect(http.post).toHaveBeenCalledWith(
        TRACE_ENDPOINTS.GET_SERVICE_ANALYTICS,
        mockGetServiceAnalyticsPayload,
      );
      expect(result).toEqual(mockUsageMatrixResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.getServiceAnalytics(mockGetServiceAnalyticsPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });
});
