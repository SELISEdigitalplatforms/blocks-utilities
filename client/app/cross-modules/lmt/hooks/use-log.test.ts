import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { mockProjectStoreFactory } from "@/test-utils/__mocks__";
import {
  mockLmtServiceFactory,
  mockLogsResponse,
  mockGetLogsPayload,
} from "../test-utils/__mocks__";
import { lmtService } from "../services/lmt.service";
import { useGetLogs, useGetLiveLogs } from "./use-log";

vi.mock("@blocks-lmt/services/lmt.service", () => mockLmtServiceFactory());
vi.mock("@/store/useProjectStore", () => mockProjectStoreFactory());

describe("use-log hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  // ─── useGetLogs ───────────────────────────────────────────────────────────
  describe("useGetLogs", () => {
    it("should fetch logs successfully", async () => {
      vi.mocked(lmtService.log.getLogs).mockResolvedValue(mockLogsResponse);

      const { result } = renderHook(() => useGetLogs(mockGetLogsPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockLogsResponse);
      expect(lmtService.log.getLogs).toHaveBeenCalledWith(mockGetLogsPayload);
    });
  });

  // ─── useGetLiveLogs ───────────────────────────────────────────────────────
  describe("useGetLiveLogs", () => {
    it("should fetch live logs successfully", async () => {
      const livePayload = {
        serviceName: "blocks-idp-api",
        lastDate: "2026-01-15T10:30:00.000Z",
        projectKey: "test-key",
      };
      vi.mocked(lmtService.log.getLiveLog).mockResolvedValue(mockLogsResponse);

      const { result } = renderHook(() => useGetLiveLogs(livePayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockLogsResponse);
      expect(lmtService.log.getLiveLog).toHaveBeenCalledWith(livePayload);
    });
  });
});
