import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { apiSettingsService } from "./api-settings.service";
import { API_SETTINGS_ENDPOINTS } from "../constants/endpoint.constant";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("apiSettingsService", () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(() => vi.clearAllMocks());

  it("getEndpoints posts with defaulted paging", async () => {
    vi.mocked(http.post).mockResolvedValue({ data: [] });
    await apiSettingsService.getEndpoints({ projectKey: "pk" } as never);
    expect(http.post).toHaveBeenCalledWith(API_SETTINGS_ENDPOINTS.GET_LIST, {
      projectKey: "pk",
      page: 0,
      pageSize: 100,
      filter: {},
    });
  });

  it("getEndpoints forwards explicit paging and filter", async () => {
    vi.mocked(http.post).mockResolvedValue({ data: [] });
    await apiSettingsService.getEndpoints({
      projectKey: "pk",
      page: 2,
      pageSize: 25,
      filter: { search: "x" },
    } as never);
    expect(http.post).toHaveBeenCalledWith(API_SETTINGS_ENDPOINTS.GET_LIST, {
      projectKey: "pk",
      page: 2,
      pageSize: 25,
      filter: { search: "x" },
    });
  });

  it("updateEndpoint posts the payload", async () => {
    vi.mocked(http.post).mockResolvedValue({ isSuccess: true });
    await apiSettingsService.updateEndpoint({ itemId: "1" } as never);
    expect(http.post).toHaveBeenCalledWith(API_SETTINGS_ENDPOINTS.UPDATE, {
      itemId: "1",
    });
  });

  it("bulkUpdate posts the payload", async () => {
    vi.mocked(http.post).mockResolvedValue({ isSuccess: true });
    await apiSettingsService.bulkUpdate({ items: [] } as never);
    expect(http.post).toHaveBeenCalledWith(
      API_SETTINGS_ENDPOINTS.BULK_UPDATE,
      { items: [] },
    );
  });

  it("removeEndpoints posts the payload", async () => {
    vi.mocked(http.post).mockResolvedValue({ isSuccess: true });
    await apiSettingsService.removeEndpoints({ ids: ["1"] } as never);
    expect(http.post).toHaveBeenCalledWith(API_SETTINGS_ENDPOINTS.REMOVE, {
      ids: ["1"],
    });
  });
});
