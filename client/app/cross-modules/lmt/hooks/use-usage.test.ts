import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { mockProjectStoreFactory } from "@/test-utils/__mocks__";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";
import {
  mockLmtServiceFactory,
  mockUsageMatrixResponse,
  mockGetOperationalAnalyticsPayload,
  mockGetServiceAnalyticsPayload,
} from "../test-utils/__mocks__";
import { lmtService } from "../services/lmt.service";
import { useGetOperationalAnalytics, useGetServiceAnalytics, useUsagesMetrics } from "./use-usage";

vi.mock("@blocks-lmt/services/lmt.service", () => mockLmtServiceFactory());
vi.mock("@/store/useProjectStore", () => mockProjectStoreFactory());

describe("use-usage hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  // ─── useGetOperationalAnalytics ───────────────────────────────────────────
  describe("useGetOperationalAnalytics", () => {
    it("should fetch operational analytics successfully", async () => {
      vi.mocked(lmtService.usage.getOperationalAnalytics).mockResolvedValue(
        mockUsageMatrixResponse,
      );

      const { result } = renderHook(
        () => useGetOperationalAnalytics(mockGetOperationalAnalyticsPayload),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockUsageMatrixResponse);
      expect(lmtService.usage.getOperationalAnalytics).toHaveBeenCalledWith(
        mockGetOperationalAnalyticsPayload,
      );
    });
  });

  // ─── useGetServiceAnalytics ───────────────────────────────────────────────
  describe("useGetServiceAnalytics", () => {
    it("should fetch service analytics successfully", async () => {
      vi.mocked(lmtService.usage.getServiceAnalytics).mockResolvedValue(mockUsageMatrixResponse);

      const { result } = renderHook(() => useGetServiceAnalytics(mockGetServiceAnalyticsPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockUsageMatrixResponse);
      expect(lmtService.usage.getServiceAnalytics).toHaveBeenCalledWith(
        mockGetServiceAnalyticsPayload,
      );
    });
  });

  // ─── useUsagesMetrics ─────────────────────────────────────────────────────
  describe("useUsagesMetrics", () => {
    it("should fetch metrics with 24h time range by default", async () => {
      vi.mocked(lmtService.usage.getServiceAnalytics).mockResolvedValue(mockUsageMatrixResponse);

      const { result } = renderHook(
        () => useUsagesMetrics({ timeRange: "24h", projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(lmtService.usage.getServiceAnalytics).toHaveBeenCalled();

      const callPayload = vi.mocked(lmtService.usage.getServiceAnalytics).mock.calls[0][0];
      expect(callPayload.projectKey).toBe(TEST_PROJECT_KEY);
      expect(callPayload.startTime).toBeDefined();
      expect(callPayload.endTime).toBeDefined();
    });

    it("should handle 1h time range", async () => {
      vi.mocked(lmtService.usage.getServiceAnalytics).mockResolvedValue(mockUsageMatrixResponse);

      const { result } = renderHook(
        () => useUsagesMetrics({ timeRange: "1h", projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(lmtService.usage.getServiceAnalytics).toHaveBeenCalled();
    });

    it("should handle 7d time range", async () => {
      vi.mocked(lmtService.usage.getServiceAnalytics).mockResolvedValue(mockUsageMatrixResponse);

      const { result } = renderHook(
        () => useUsagesMetrics({ timeRange: "7d", projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(lmtService.usage.getServiceAnalytics).toHaveBeenCalled();
    });

    it("should handle 30d time range", async () => {
      vi.mocked(lmtService.usage.getServiceAnalytics).mockResolvedValue(mockUsageMatrixResponse);

      const { result } = renderHook(
        () => useUsagesMetrics({ timeRange: "30d", projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(lmtService.usage.getServiceAnalytics).toHaveBeenCalled();
    });

    it("should default to 24h for unrecognized time range", async () => {
      vi.mocked(lmtService.usage.getServiceAnalytics).mockResolvedValue(mockUsageMatrixResponse);

      const { result } = renderHook(
        () => useUsagesMetrics({ timeRange: "unknown", projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(lmtService.usage.getServiceAnalytics).toHaveBeenCalled();
    });

    it("should return normalized metrics data", async () => {
      vi.mocked(lmtService.usage.getServiceAnalytics).mockResolvedValue(mockUsageMatrixResponse);

      const { result } = renderHook(
        () => useUsagesMetrics({ timeRange: "24h", projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toBeDefined();
      expect(result.current.data?.services).toBeDefined();
      expect(typeof result.current.data?.accumulatedApiCall).toBe("number");
    });
  });
});
