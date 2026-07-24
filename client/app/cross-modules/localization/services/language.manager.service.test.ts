import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
import { languageManagerService } from "./language.manager.service";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("languageManagerService", () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(() => vi.clearAllMocks());

  const baseKeyRequest = {
    projectKey: "pk",
    pageNumber: 1,
    pageSize: 10,
    searchKey: "",
    moduleIds: [],
    isPartiallyTranslated: false,
    sortProperty: "keyName",
    isDescending: false,
  };

  it("fetchBlocksLanguageKey posts and strips empty date ranges", async () => {
    vi.mocked(http.post).mockResolvedValue({ totalCount: 0, keys: [] });
    await languageManagerService.fetchBlocksLanguageKey({
      ...baseKeyRequest,
      createDateRange: { startDate: "", endDate: "2023-01-01" },
      lastUpdateDateRange: { startDate: "", endDate: "2023-01-01" },
    });
    const payload = vi.mocked(http.post).mock.calls[0][1] as Record<
      string,
      unknown
    >;
    expect(payload.createDateRange).toBeDefined();
  });

  it("fetchBlocksLanguageKey deletes missing date ranges entirely", async () => {
    vi.mocked(http.post).mockResolvedValue({ totalCount: 0, keys: [] });
    await languageManagerService.fetchBlocksLanguageKey(baseKeyRequest);
    const payload = vi.mocked(http.post).mock.calls[0][1] as Record<
      string,
      unknown
    >;
    expect(payload.createDateRange).toBeUndefined();
    expect(payload.lastUpdateDateRange).toBeUndefined();
  });

  it("fetchBlocksLanguageKeyById gets by project and item id", async () => {
    vi.mocked(http.get).mockResolvedValue({ itemId: "i" });
    await languageManagerService.fetchBlocksLanguageKeyById({
      projectKey: "pk",
      itemId: "i",
    });
    const url = vi.mocked(http.get).mock.calls[0][0] as string;
    expect(url).toContain("projectKey=pk");
    expect(url).toContain("itemId=i");
  });

  it("fetchBlocksLanguageModules gets modules by project", async () => {
    vi.mocked(http.get).mockResolvedValue([]);
    await languageManagerService.fetchBlocksLanguageModules("pk");
    expect((vi.mocked(http.get).mock.calls[0][0] as string)).toContain(
      "projectKey=pk",
    );
  });

  it("fetchBlocksLanguages gets languages by project", async () => {
    vi.mocked(http.get).mockResolvedValue([]);
    await languageManagerService.fetchBlocksLanguages("pk");
    expect((vi.mocked(http.get).mock.calls[0][0] as string)).toContain(
      "projectKey=pk",
    );
  });

  it("saveBlocksLanguageKey defaults isNewKey to false", async () => {
    vi.mocked(http.post).mockResolvedValue({ success: true });
    await languageManagerService.saveBlocksLanguageKey({
      itemId: "i",
      keyName: "k",
      moduleId: "m",
      resources: [],
      routes: [],
      isPartiallyTranslated: false,
      projectKey: "pk",
    });
    const payload = vi.mocked(http.post).mock.calls[0][1] as Record<
      string,
      unknown
    >;
    expect(payload.isNewKey).toBe(false);
  });

  it("saveLanguageModule posts the payload", async () => {
    vi.mocked(http.post).mockResolvedValue({ success: true });
    await languageManagerService.saveLanguageModule({
      moduleName: "m",
      projectKey: "pk",
    });
    expect(http.post).toHaveBeenCalled();
  });

  it("getLanguageModule gets modules by project", async () => {
    vi.mocked(http.get).mockResolvedValue([]);
    await languageManagerService.getLanguageModule("pk");
    expect((vi.mocked(http.get).mock.calls[0][0] as string)).toContain(
      "ProjectKey=pk",
    );
  });

  it("saveLanguage posts the payload", async () => {
    vi.mocked(http.post).mockResolvedValue({ success: true });
    await languageManagerService.saveLanguage({
      languageName: "English",
      languageCode: "en",
      projectKey: "pk",
    });
    expect(http.post).toHaveBeenCalled();
  });

  it("deleteLanguageKey deletes by item id", async () => {
    vi.mocked(http.delete).mockResolvedValue({ isSuccess: true });
    const result = await languageManagerService.deleteLanguageKey({
      itemId: "i",
      projectKey: "pk",
    });
    expect((vi.mocked(http.delete).mock.calls[0][0] as string)).toContain(
      "itemId=i",
    );
    expect(result).toEqual({ isSuccess: true });
  });

  it("deleteLanguage deletes by language name", async () => {
    vi.mocked(http.delete).mockResolvedValue({ isSuccess: true });
    const result = await languageManagerService.deleteLanguage({
      languageName: "English",
      projectKey: "pk",
    });
    expect((vi.mocked(http.delete).mock.calls[0][0] as string)).toContain(
      "languageName=English",
    );
    expect(result).toEqual({ isSuccess: true });
  });

  it("setDefault posts the payload", async () => {
    vi.mocked(http.post).mockResolvedValue({ isSuccess: true });
    await languageManagerService.setDefault({
      languageName: "English",
      projectKey: "pk",
    });
    expect(http.post).toHaveBeenCalled();
  });

  it("generateUilmFile posts the payload", async () => {
    vi.mocked(http.post).mockResolvedValue({ isSuccess: true });
    await languageManagerService.generateUilmFile({
      guid: "g",
      projectKey: "pk",
    });
    expect(http.post).toHaveBeenCalled();
  });

  it("getTranslationSuggestion posts the payload", async () => {
    vi.mocked(http.post).mockResolvedValue({ content: "hi", isSuccess: true });
    const result = await languageManagerService.getTranslationSuggestion({
      sourceText: "hello",
      destinationLanguage: "de",
      currentLanguage: "en",
      temperature: 0.5,
      elementDetailContext: "ctx",
    });
    expect(result.content).toBe("hi");
  });

  it("translateAll posts the payload", async () => {
    vi.mocked(http.post).mockResolvedValue({ isSuccess: true });
    await languageManagerService.translateAll({
      projectKey: "pk",
      messageCoRelationId: "c",
      defaultLanguage: "en",
    });
    expect(http.post).toHaveBeenCalled();
  });

  it("translateKey posts the payload", async () => {
    vi.mocked(http.post).mockResolvedValue({ isSuccess: true });
    await languageManagerService.translateKey({
      keyId: "k",
      projectKey: "pk",
      defaultLanguage: "en",
      messageCoRelationId: "c",
    });
    expect(http.post).toHaveBeenCalled();
  });

  it("importLanguageFile posts the payload", async () => {
    vi.mocked(http.post).mockResolvedValue({ isSuccess: true });
    await languageManagerService.importLanguageFile({ projectKey: "pk" } as never);
    expect(http.post).toHaveBeenCalled();
  });

  it("saveLanguageKeyUilmExport posts the payload", async () => {
    vi.mocked(http.post).mockResolvedValue({ isSuccess: true });
    await languageManagerService.saveLanguageKeyUilmExport({
      projectKey: "pk",
    } as never);
    expect(http.post).toHaveBeenCalled();
  });

  it("getKeysTimeline builds the timeline url", async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] });
    await languageManagerService.getKeysTimeline({
      pageNumber: 1,
      pageSize: 10,
      keyId: "k",
      projectKey: "pk",
    });
    const url = vi.mocked(http.get).mock.calls[0][0] as string;
    expect(url).toContain("EntityId=k");
    expect(url).toContain("projectKey=pk");
  });

  it("getExportHistory appends optional filters", async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] });
    await languageManagerService.getExportHistory({
      projectKey: "pk",
      pageNumber: 1,
      pageSize: 10,
      filters: {
        searchText: "abc",
        startDate: "2023-01-01",
        endDate: "2023-02-01",
      } as never,
    });
    const url = vi.mocked(http.get).mock.calls[0][0] as string;
    expect(url).toContain("SearchText=abc");
    expect(url).toContain("CreateDateRange.StartDate=2023-01-01");
    expect(url).toContain("CreateDateRange.EndDate=2023-02-01");
  });

  it("getExportHistory omits absent filters", async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] });
    await languageManagerService.getExportHistory({
      projectKey: "pk",
      pageNumber: 1,
      pageSize: 10,
      filters: {} as never,
    });
    const url = vi.mocked(http.get).mock.calls[0][0] as string;
    expect(url).not.toContain("SearchText");
  });

  it("revertKeyTimeline posts the rollback payload", async () => {
    vi.mocked(http.post).mockResolvedValue({ isSuccess: true });
    await languageManagerService.revertKeyTimeline({
      itemId: "i",
      projectKey: "pk",
    });
    expect(http.post).toHaveBeenCalled();
  });

  it("getLocalizationTimeline appends every optional param", async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] });
    await languageManagerService.getLocalizationTimeline({
      projectKey: "pk",
      pageNumber: 1,
      pageSize: 10,
      userId: "u",
      logFrom: "web",
      logFromValues: ["a", "b"],
      excludeLogFromValues: ["c"],
      createDateRange: { startDate: "2023-01-01", endDate: "2023-02-01" },
    });
    const url = vi.mocked(http.get).mock.calls[0][0] as string;
    expect(url).toContain("UserId=u");
    expect(url).toContain("LogFrom=web");
    expect(url).toContain("LogFromValues=a");
    expect(url).toContain("ExcludeLogFromValues=c");
    expect(url).toContain("CreateDateRange.StartDate=2023-01-01");
  });

  it("getLocalizationTimeline omits absent optional params", async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] });
    await languageManagerService.getLocalizationTimeline({
      projectKey: "pk",
      pageNumber: 1,
      pageSize: 10,
    });
    const url = vi.mocked(http.get).mock.calls[0][0] as string;
    expect(url).not.toContain("UserId");
  });

  it("getTimelineByOperationId builds the operation url", async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] });
    await languageManagerService.getTimelineByOperationId({
      operationId: "op",
      projectKey: "pk",
      pageNumber: 1,
      pageSize: 10,
    });
    const url = vi.mocked(http.get).mock.calls[0][0] as string;
    expect(url).toContain("OperationId=op");
  });
});
