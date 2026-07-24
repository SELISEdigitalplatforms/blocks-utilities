import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { apiSettingsService } from "../services/api-settings.service";
import {
  useGetApiEndpoints,
  useUpdateApiEndpoint,
  useBulkUpdateApiEndpoints,
  useRemoveApiEndpoints,
} from "./use-api-settings";

vi.mock("../services/api-settings.service", () => ({
  apiSettingsService: {
    getEndpoints: vi.fn(),
    updateEndpoint: vi.fn(),
    bulkUpdate: vi.fn(),
    removeEndpoints: vi.fn(),
  },
}));

describe("use-api-settings hooks", () => {
  beforeEach(() => vi.clearAllMocks());

  it("useGetApiEndpoints fetches with a project key", async () => {
    vi.mocked(apiSettingsService.getEndpoints).mockResolvedValue({
      data: [],
    } as never);
    const { result } = renderHook(
      () => useGetApiEndpoints({ projectKey: "pk", page: 1, pageSize: 10 } as never),
      { wrapper: createWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiSettingsService.getEndpoints).toHaveBeenCalled();
  });

  it("useGetApiEndpoints is disabled without a project key", () => {
    const { result } = renderHook(
      () => useGetApiEndpoints({ projectKey: "", page: 1, pageSize: 10 } as never),
      { wrapper: createWrapper() },
    );
    expect(result.current.fetchStatus).toBe("idle");
  });

  it("useUpdateApiEndpoint mutates", async () => {
    vi.mocked(apiSettingsService.updateEndpoint).mockResolvedValue({} as never);
    const { result } = renderHook(() => useUpdateApiEndpoint(), {
      wrapper: createWrapper(),
    });
    result.current.mutate({} as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiSettingsService.updateEndpoint).toHaveBeenCalled();
  });

  it("useBulkUpdateApiEndpoints mutates", async () => {
    vi.mocked(apiSettingsService.bulkUpdate).mockResolvedValue({} as never);
    const { result } = renderHook(() => useBulkUpdateApiEndpoints(), {
      wrapper: createWrapper(),
    });
    result.current.mutate({} as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiSettingsService.bulkUpdate).toHaveBeenCalled();
  });

  it("useRemoveApiEndpoints mutates", async () => {
    vi.mocked(apiSettingsService.removeEndpoints).mockResolvedValue({} as never);
    const { result } = renderHook(() => useRemoveApiEndpoints(), {
      wrapper: createWrapper(),
    });
    result.current.mutate({} as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiSettingsService.removeEndpoints).toHaveBeenCalled();
  });
});
