import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { magicUrlService } from "@blocks-utilities/magic-url/services/magic-url.service";
import {
  useGetMagicUrls,
  useGetMagicUrlById,
  useCreateMagicUrl,
  useSaveMagicUrlConfig,
  useGetMagicUrlConfig,
  useRemoveMagicUrl,
} from "./use-magic-url";

vi.mock("@blocks-utilities/magic-url/services/magic-url.service", () => ({
  magicUrlService: {
    getMagicUrls: vi.fn(),
    getMagicUrl: vi.fn(),
    createMagicUrl: vi.fn(),
    saveMagicUrlConfig: vi.fn(),
    getMagicUrlConfig: vi.fn(),
    deactivateMagicLinks: vi.fn(),
  },
}));

describe("use-magic-url hooks", () => {
  beforeEach(() => vi.clearAllMocks());

  it("useGetMagicUrls fetches when a project key is present", async () => {
    vi.mocked(magicUrlService.getMagicUrls).mockResolvedValue({
      data: [],
      errors: [],
      totalCount: 0,
    });
    const { result } = renderHook(
      () => useGetMagicUrls({ page: 1, pageSize: 10, projectKey: "pk" } as never),
      { wrapper: createWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(magicUrlService.getMagicUrls).toHaveBeenCalled();
  });

  it("useGetMagicUrls is disabled without a project key", () => {
    const { result } = renderHook(
      () => useGetMagicUrls({ page: 1, pageSize: 10, projectKey: "" } as never),
      { wrapper: createWrapper() },
    );
    expect(result.current.fetchStatus).toBe("idle");
  });

  it("useGetMagicUrlById fetches by id", async () => {
    vi.mocked(magicUrlService.getMagicUrl).mockResolvedValue({
      itemId: "1",
    } as never);
    const { result } = renderHook(
      () => useGetMagicUrlById({ ItemId: "1", projectKey: "pk" } as never),
      { wrapper: createWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({ itemId: "1" });
  });

  it("useCreateMagicUrl mutates", async () => {
    vi.mocked(magicUrlService.createMagicUrl).mockResolvedValue({
      itemId: "1",
    } as never);
    const { result } = renderHook(() => useCreateMagicUrl(), {
      wrapper: createWrapper(),
    });
    result.current.mutate({ uri: "x" } as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(magicUrlService.createMagicUrl).toHaveBeenCalled();
  });

  it("useSaveMagicUrlConfig mutates", async () => {
    vi.mocked(magicUrlService.saveMagicUrlConfig).mockResolvedValue({
      isSuccess: true,
    } as never);
    const { result } = renderHook(() => useSaveMagicUrlConfig(), {
      wrapper: createWrapper(),
    });
    result.current.mutate({ projectKey: "pk" } as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(magicUrlService.saveMagicUrlConfig).toHaveBeenCalled();
  });

  it("useGetMagicUrlConfig fetches by project key", async () => {
    vi.mocked(magicUrlService.getMagicUrlConfig).mockResolvedValue({
      isSuccess: true,
    } as never);
    const { result } = renderHook(() => useGetMagicUrlConfig("pk"), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(magicUrlService.getMagicUrlConfig).toHaveBeenCalledWith("pk");
  });

  it("useGetMagicUrlConfig respects an explicit disabled option", () => {
    const { result } = renderHook(
      () => useGetMagicUrlConfig("pk", { enabled: false }),
      { wrapper: createWrapper() },
    );
    expect(result.current.fetchStatus).toBe("idle");
  });

  it("useRemoveMagicUrl mutates", async () => {
    vi.mocked(magicUrlService.deactivateMagicLinks).mockResolvedValue(undefined);
    const { result } = renderHook(() => useRemoveMagicUrl(), {
      wrapper: createWrapper(),
    });
    result.current.mutate({ linkIds: ["1"], projectKey: "pk" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(magicUrlService.deactivateMagicLinks).toHaveBeenCalled();
  });
});
