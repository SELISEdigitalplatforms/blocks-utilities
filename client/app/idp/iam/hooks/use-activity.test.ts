import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { mockToastFactory } from "@/test-utils/__mocks__";
import {
  mockUserServiceFactory,
  mockGetSessionsPayload,
  mockGetHistoriesPayload,
  mockGeneratePATPayload,
} from "../../test-utils/__mocks__";
import { userService } from "@blocks-idp/iam/services/user.service";
import { useGetSessions, useGetHistories, useGetPats, useGeneratePats } from "./use-activity";

vi.mock("@blocks-idp/iam/services/user.service", () => mockUserServiceFactory());
vi.mock("@/hooks/use-toast", () => mockToastFactory());

describe("use-activity hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useGetSessions", () => {
    it("should fetch sessions successfully", async () => {
      const mockResponse = { data: [], totalCount: 0, errors: null };
      vi.mocked(userService.getSessions).mockResolvedValue(mockResponse as never);

      const { result } = renderHook(() => useGetSessions(mockGetSessionsPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResponse);
      expect(userService.getSessions).toHaveBeenCalledWith(mockGetSessionsPayload);
    });
  });

  describe("useGetHistories", () => {
    it("should fetch histories successfully", async () => {
      const mockResponse = { data: [], totalCount: 0, errors: null };
      vi.mocked(userService.getHistories).mockResolvedValue(mockResponse as never);

      const { result } = renderHook(() => useGetHistories(mockGetHistoriesPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResponse);
      expect(userService.getHistories).toHaveBeenCalledWith(mockGetHistoriesPayload);
    });
  });

  describe("useGetPats", () => {
    it("should fetch personal access tokens successfully", async () => {
      const mockPats = [
        { createdDate: "2026-01-15T10:00:00Z", note: "Token 1" },
        { createdDate: "2026-01-14T10:00:00Z", note: "Token 2" },
      ];
      vi.mocked(userService.getPats).mockResolvedValue(mockPats as never);

      const { result } = renderHook(() => useGetPats(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(userService.getPats).toHaveBeenCalled();
    });

    it("should sort PATs by createdDate descending", async () => {
      const mockPats = [
        { createdDate: "2026-01-14T10:00:00Z", note: "Older" },
        { createdDate: "2026-01-15T10:00:00Z", note: "Newer" },
      ];
      vi.mocked(userService.getPats).mockResolvedValue(mockPats as never);

      const { result } = renderHook(() => useGetPats(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data![0].note).toBe("Newer");
      expect(result.current.data![1].note).toBe("Older");
    });

    it("should return empty array when data is null", async () => {
      vi.mocked(userService.getPats).mockResolvedValue(null as never);

      const { result } = renderHook(() => useGetPats(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual([]);
    });
  });

  describe("useGeneratePats", () => {
    it("should generate PAT successfully", async () => {
      const mockResponse = { token: "pat-token-123" };
      vi.mocked(userService.generatePats).mockResolvedValue(mockResponse as never);

      const { result } = renderHook(() => useGeneratePats(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockGeneratePATPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(userService.generatePats).toHaveBeenCalledWith(mockGeneratePATPayload);
    });
  });
});
