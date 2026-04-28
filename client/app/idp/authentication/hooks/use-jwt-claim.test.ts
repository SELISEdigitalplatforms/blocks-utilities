import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { mockJwtClaimPayload } from "../../test-utils/__mocks__";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__";
import { projectService } from "@blocks-identifier/services/project.service";
import { useAddJwtClaim, useGetJwtClaim } from "./use-jwt-claim";

vi.mock("@blocks-identifier/services/project.service", () => ({
  projectService: {
    addJwtClaim: vi.fn(),
    getJwtClaim: vi.fn(),
  },
}));

describe("use-jwt-claim hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useAddJwtClaim", () => {
    it("should add JWT claim successfully", async () => {
      vi.mocked(projectService.addJwtClaim).mockResolvedValue(undefined as never);

      const { result } = renderHook(() => useAddJwtClaim(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockJwtClaimPayload);
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(projectService.addJwtClaim).toHaveBeenCalledWith(mockJwtClaimPayload);
    });
  });

  describe("useGetJwtClaim", () => {
    it("should fetch JWT claim successfully", async () => {
      const mockResponse = { data: { claims: [] } };
      const payload = { projectKey: TEST_PROJECT_KEY, itemId: "test-item-id" };
      vi.mocked(projectService.getJwtClaim).mockResolvedValue(mockResponse as never);

      const { result } = renderHook(() => useGetJwtClaim(payload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResponse);
      expect(projectService.getJwtClaim).toHaveBeenCalledWith(payload);
    });

    it("should not fetch when projectKey is empty", () => {
      const { result } = renderHook(() => useGetJwtClaim({ projectKey: "", itemId: "" }), {
        wrapper: createWrapper(),
      });

      expect(result.current.fetchStatus).toBe("idle");
    });
  });
});
