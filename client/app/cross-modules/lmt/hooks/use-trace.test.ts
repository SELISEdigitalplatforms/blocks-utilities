import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { mockProjectStoreFactory } from "@/test-utils/__mocks__";
import {
  mockLmtServiceFactory,
  mockGetTracesPayload,
  mockGetTraceByIdPayload,
} from "../test-utils/__mocks__";
import type { IAPIResponse } from "@/models/api-response";
import type { TraceTree } from "../models/trace.model";
import { lmtService } from "../services/lmt.service";
import { useGetTraces, useGetTraceById } from "./use-trace";

vi.mock("@blocks-lmt/services/lmt.service", () => mockLmtServiceFactory());
vi.mock("@/store/useProjectStore", () => mockProjectStoreFactory());

describe("use-trace hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  // ─── useGetTraces ─────────────────────────────────────────────────────────
  describe("useGetTraces", () => {
    it("should fetch traces successfully", async () => {
      const mockResponse = { data: [], errors: [], totalCount: 0 };
      vi.mocked(lmtService.trace.getTraces).mockResolvedValue(mockResponse);

      const { result } = renderHook(() => useGetTraces(mockGetTracesPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResponse);
      expect(lmtService.trace.getTraces).toHaveBeenCalledWith(mockGetTracesPayload);
    });
  });

  // ─── useGetTraceById ──────────────────────────────────────────────────────
  describe("useGetTraceById", () => {
    it("should fetch a trace by ID successfully", async () => {
      const mockResponse = { data: {} as TraceTree, errors: [], totalCount: 0 } as IAPIResponse<TraceTree>;
      vi.mocked(lmtService.trace.getTraceByTraceId).mockResolvedValue(mockResponse);

      const { result } = renderHook(() => useGetTraceById(mockGetTraceByIdPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResponse);
      expect(lmtService.trace.getTraceByTraceId).toHaveBeenCalledWith(mockGetTraceByIdPayload);
    });
  });
});
