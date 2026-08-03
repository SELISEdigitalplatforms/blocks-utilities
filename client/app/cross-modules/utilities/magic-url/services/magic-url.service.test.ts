import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { serviceInstances } from "@/lib/http-client";
import { MagicUrlService } from "./magic-url.service";
import { MAGIC_URL_ENDPOINTS } from "@blocks-utilities/magic-url/constants/endpoint.constant";

const http = {
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  patch: vi.fn(),
  delete: vi.fn(),
};

vi.mock("@/lib/http-client", () => ({
  serviceInstances: {
    idpService: {},
    logicService: {},
    get utitlitiesService() {
      return (globalThis as Record<string, unknown>).__http__;
    },
  },
}));

(globalThis as Record<string, unknown>).__http__ = http;

describe("MagicUrlService", () => {
  let service: MagicUrlService;

  beforeEach(() => {
    service = new MagicUrlService();
    vi.clearAllMocks();
  });
  afterEach(() => vi.clearAllMocks());

  it("getMagicUrl returns the unwrapped data", async () => {
    http.get.mockResolvedValue({ data: { itemId: "1" } });
    const result = await service.getMagicUrl({
      ItemId: "1",
      projectKey: "pk",
    } as never);
    expect(serviceInstances.utitlitiesService.get).toHaveBeenCalledWith(
      `${MAGIC_URL_ENDPOINTS.GET_LINK}?ItemId=1&ProjectKey=pk`,
    );
    expect(result).toEqual({ itemId: "1" });
  });

  it("getMagicUrl rethrows on failure", async () => {
    http.get.mockRejectedValue(new Error("boom"));
    await expect(
      service.getMagicUrl({ ItemId: "1", projectKey: "pk" } as never),
    ).rejects.toThrow("boom");
  });

  it("getMagicUrls builds query params and normalizes the response", async () => {
    http.get.mockResolvedValue({ data: [{ id: 1 }], errors: null, totalCount: 5 });
    const result = await service.getMagicUrls({
      page: 2,
      pageSize: 10,
      projectKey: "pk",
      searchText: "abc",
      status: "Active",
      requestMethod: "GET",
      type: "Redirect",
      expiryDateRangeStartDate: "2023-01-01",
      expiryDateRangeEndDate: "2023-02-01",
    } as never);
    const calledUrl = http.get.mock.calls[0][0] as string;
    expect(calledUrl).toContain("PageSize=10");
    expect(calledUrl).toContain("PageNumber=2");
    expect(calledUrl).toContain("SearchText=abc");
    expect(calledUrl).toContain("Status=Active");
    expect(calledUrl).toContain("RequestMethod=GET");
    expect(calledUrl).toContain("Type=Redirect");
    expect(calledUrl).toContain("ExpiryDateRange.StartDate=2023-01-01");
    expect(result).toEqual({ data: [{ id: 1 }], errors: [], totalCount: 5 });
  });

  it("getMagicUrls falls back to empty errors/count", async () => {
    http.get.mockResolvedValue({ data: [] });
    const result = await service.getMagicUrls({
      page: 1,
      pageSize: 10,
      projectKey: "pk",
    } as never);
    expect(result.errors).toEqual([]);
    expect(result.totalCount).toBe(0);
  });

  it("getMagicUrls rethrows on failure", async () => {
    http.get.mockRejectedValue(new Error("boom"));
    await expect(
      service.getMagicUrls({ page: 1, pageSize: 10, projectKey: "pk" } as never),
    ).rejects.toThrow("boom");
  });

  it("createMagicUrl posts the payload", async () => {
    http.post.mockResolvedValue({ itemId: "1" });
    const result = await service.createMagicUrl({ uri: "x" } as never);
    expect(http.post).toHaveBeenCalledWith(
      MAGIC_URL_ENDPOINTS.CREATE_LINK,
      { uri: "x" },
    );
    expect(result).toEqual({ itemId: "1" });
  });

  it("createMagicUrl rethrows on failure", async () => {
    http.post.mockRejectedValue(new Error("boom"));
    await expect(service.createMagicUrl({} as never)).rejects.toThrow("boom");
  });

  it("saveMagicUrlConfig posts to the config endpoint", async () => {
    http.post.mockResolvedValue({ isSuccess: true });
    const result = await service.saveMagicUrlConfig({ projectKey: "pk" } as never);
    expect(http.post).toHaveBeenCalledWith(
      MAGIC_URL_ENDPOINTS.SAVE_CONFIG,
      { projectKey: "pk" },
    );
    expect(result).toEqual({ isSuccess: true });
  });

  it("saveMagicUrlConfig rethrows on failure", async () => {
    http.post.mockRejectedValue(new Error("boom"));
    await expect(service.saveMagicUrlConfig({} as never)).rejects.toThrow("boom");
  });

  it("getMagicUrlConfig gets by project key", async () => {
    http.get.mockResolvedValue({ isSuccess: true });
    const result = await service.getMagicUrlConfig("pk");
    expect(http.get).toHaveBeenCalledWith(
      `${MAGIC_URL_ENDPOINTS.GET_CONFIG}?ProjectKey=pk`,
    );
    expect(result).toEqual({ isSuccess: true });
  });

  it("getMagicUrlConfig rethrows on failure", async () => {
    http.get.mockRejectedValue(new Error("boom"));
    await expect(service.getMagicUrlConfig("pk")).rejects.toThrow("boom");
  });

  it("deactivateMagicLinks posts the ids", async () => {
    http.post.mockResolvedValue(undefined);
    await service.deactivateMagicLinks({ linkIds: ["a"], projectKey: "pk" });
    expect(http.post).toHaveBeenCalledWith(MAGIC_URL_ENDPOINTS.REMOVE_LINKS, {
      linkIds: ["a"],
      projectKey: "pk",
    });
  });

  it("deactivateMagicLinks rethrows on failure", async () => {
    http.post.mockRejectedValue(new Error("boom"));
    await expect(
      service.deactivateMagicLinks({ linkIds: [], projectKey: "pk" }),
    ).rejects.toThrow("boom");
  });
});
