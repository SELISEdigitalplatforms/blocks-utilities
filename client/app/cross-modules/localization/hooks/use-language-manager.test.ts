import { createWrapper } from "@/test-utils/test-providers/query-client";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { mockProjectStoreFactory, TEST_TENANT_ID, TEST_PROJECT_KEY } from "@/test-utils/__mocks__";
import {
  mockLanguageServiceFactory,
  mockLanguageKeysResponse,
  mockBlocksLanguageKey,
  mockLanguageModuleList,
  mockLanguageConfigList,
  mockSuccessResponse,
  mockDeleteSuccessResponse,
  mockGetTimelineResponse,
  mockGetExportHistory,
  mockRollbackResponse,
  mockModuleGetsList,
  mockSaveLanguageKeyPayload,
  mockSaveLanguageModulePayload,
  mockSaveLanguagePayload,
  mockDeleteLanguageKeyPayload,
  mockDeleteLanguagePayload,
  mockSetDefaultPayload,
  mockTranslateAllPayload,
  mockTranslateKeyPayload,
  mockImportFile,
  mockKeyUilmExport,
  mockGenerateUilmFilePayload,
  mockTranslationSuggestionPayload,
  mockTranslationSuggestionResponse,
  mockRevertKeyTimelinePayload,
} from "../test-utils/__mocks__";
import { languageManagerService } from "@blocks-localization/services/language.manager.service";
import {
  useGetBlocksLanguageKey,
  useGetBlocksLanguageKeyById,
  useGetLanguageModules,
  useGetLanguages,
  useSaveBlocksLanguageKey,
  useSaveLanguageModule,
  useGetLanguageModule,
  useTranslateAll,
  useTranslateKey,
  useSaveLanguage,
  useDeleteLanguageKey,
  useDeleteLanguage,
  useSetDefaultLanguage,
  useGenerateUilmFile,
  useGetTranslationSuggestion,
  useImportLanguageFile,
  useSaveLanguageKeyUilmExport,
  useGetLanguageKeysTimeline,
  useGetExportHistory,
  useRevertKeyTimeline,
} from "./use-language-manager";

vi.mock("@blocks-localization/services/language.manager.service", () =>
  mockLanguageServiceFactory(),
);
vi.mock("@/store/useProjectStore", () => mockProjectStoreFactory());

describe("Language Manager Hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  // ─── useGetBlocksLanguageKey ───────────────────────────────────────────────

  describe("useGetBlocksLanguageKey", () => {
    it("should fetch language keys successfully", async () => {
      vi.mocked(languageManagerService.fetchBlocksLanguageKey).mockResolvedValue(
        mockLanguageKeysResponse,
      );

      const { result } = renderHook(() => useGetBlocksLanguageKey(1, 10, "", [], false), {
        wrapper: createWrapper(),
      });

      expect(result.current.isLoading).toBe(true);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockLanguageKeysResponse);
      expect(languageManagerService.fetchBlocksLanguageKey).toHaveBeenCalledWith(
        expect.objectContaining({
          projectKey: TEST_TENANT_ID,
          pageNumber: 1,
          pageSize: 10,
          searchKey: "",
          moduleIds: [],
          isPartiallyTranslated: false,
        }),
      );
    });

    it("should pass all filter arguments to the service", async () => {
      vi.mocked(languageManagerService.fetchBlocksLanguageKey).mockResolvedValue(
        mockLanguageKeysResponse,
      );

      renderHook(
        () =>
          useGetBlocksLanguageKey(2, 20, "save", ["module-1"], true, "", false, {
            startDate: "2024-01-01",
            endDate: "2024-01-31",
          }),
        { wrapper: createWrapper() },
      );

      await waitFor(() =>
        expect(languageManagerService.fetchBlocksLanguageKey).toHaveBeenCalledWith(
          expect.objectContaining({
            pageNumber: 2,
            pageSize: 20,
            searchKey: "save",
            moduleIds: ["module-1"],
            isPartiallyTranslated: true,
            createDateRange: { startDate: "2024-01-01", endDate: "2024-01-31" },
          }),
        ),
      );
    });

    it("should handle errors", async () => {
      vi.mocked(languageManagerService.fetchBlocksLanguageKey).mockRejectedValue(
        new Error("Fetch failed"),
      );

      const { result } = renderHook(() => useGetBlocksLanguageKey(1, 10, "", [], false), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useGetBlocksLanguageKeyById ──────────────────────────────────────────

  describe("useGetBlocksLanguageKeyById", () => {
    it("should fetch a language key by id successfully", async () => {
      vi.mocked(languageManagerService.fetchBlocksLanguageKeyById).mockResolvedValue(
        mockBlocksLanguageKey,
      );

      const { result } = renderHook(() => useGetBlocksLanguageKeyById("key-1"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockBlocksLanguageKey);
      expect(languageManagerService.fetchBlocksLanguageKeyById).toHaveBeenCalledWith({
        projectKey: TEST_TENANT_ID,
        itemId: "key-1",
      });
    });
  });

  // ─── useGetLanguageModules ────────────────────────────────────────────────

  describe("useGetLanguageModules", () => {
    it("should fetch language modules successfully", async () => {
      vi.mocked(languageManagerService.fetchBlocksLanguageModules).mockResolvedValue(
        mockLanguageModuleList,
      );

      const { result } = renderHook(() => useGetLanguageModules(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockLanguageModuleList);
      expect(languageManagerService.fetchBlocksLanguageModules).toHaveBeenCalledWith(
        TEST_TENANT_ID,
      );
    });
  });

  // ─── useGetLanguages ──────────────────────────────────────────────────────

  describe("useGetLanguages", () => {
    it("should fetch languages successfully", async () => {
      vi.mocked(languageManagerService.fetchBlocksLanguages).mockResolvedValue(
        mockLanguageConfigList,
      );

      const { result } = renderHook(() => useGetLanguages(), { wrapper: createWrapper() });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockLanguageConfigList);
      expect(languageManagerService.fetchBlocksLanguages).toHaveBeenCalledWith(TEST_TENANT_ID);
    });

    it("should return empty array when no languages exist", async () => {
      vi.mocked(languageManagerService.fetchBlocksLanguages).mockResolvedValue([]);

      const { result } = renderHook(() => useGetLanguages(), { wrapper: createWrapper() });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual([]);
    });

    it("should handle errors", async () => {
      vi.mocked(languageManagerService.fetchBlocksLanguages).mockRejectedValue(
        new Error("Fetch failed"),
      );

      const { result } = renderHook(() => useGetLanguages(), { wrapper: createWrapper() });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useGetLanguageModule ─────────────────────────────────────────────────

  describe("useGetLanguageModule", () => {
    it("should fetch language module when projectKey is provided", async () => {
      vi.mocked(languageManagerService.getLanguageModule).mockResolvedValue(mockModuleGetsList);

      const { result } = renderHook(() => useGetLanguageModule(TEST_PROJECT_KEY), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockModuleGetsList);
      expect(languageManagerService.getLanguageModule).toHaveBeenCalledWith(TEST_PROJECT_KEY);
    });

    it("should be disabled when projectKey is empty string", async () => {
      vi.mocked(languageManagerService.getLanguageModule).mockResolvedValue(mockModuleGetsList);

      const { result } = renderHook(() => useGetLanguageModule(""), {
        wrapper: createWrapper(),
      });

      // Should remain pending (not fetching) because enabled: !!projectKey = false
      expect(result.current.isLoading).toBe(false);
      expect(result.current.fetchStatus).toBe("idle");
      expect(languageManagerService.getLanguageModule).not.toHaveBeenCalled();
    });
  });

  // ─── useGetLanguageKeysTimeline ───────────────────────────────────────────

  describe("useGetLanguageKeysTimeline", () => {
    it("should fetch timeline successfully", async () => {
      vi.mocked(languageManagerService.getKeysTimeline).mockResolvedValue(mockGetTimelineResponse);

      const { result } = renderHook(() => useGetLanguageKeysTimeline(1, 10, "key-1"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockGetTimelineResponse);
      expect(languageManagerService.getKeysTimeline).toHaveBeenCalledWith({
        projectKey: TEST_TENANT_ID,
        pageNumber: 1,
        pageSize: 10,
        keyId: "key-1",
      });
    });
  });

  // ─── useGetExportHistory ──────────────────────────────────────────────────

  describe("useGetExportHistory", () => {
    it("should fetch export history successfully", async () => {
      vi.mocked(languageManagerService.getExportHistory).mockResolvedValue(mockGetExportHistory);

      const { result } = renderHook(() => useGetExportHistory(1, 10, TEST_PROJECT_KEY, {}), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockGetExportHistory);
      expect(languageManagerService.getExportHistory).toHaveBeenCalledWith({
        projectKey: TEST_PROJECT_KEY,
        pageNumber: 1,
        pageSize: 10,
        filters: {},
      });
    });

    it("should pass all filters to the service", async () => {
      vi.mocked(languageManagerService.getExportHistory).mockResolvedValue(mockGetExportHistory);

      renderHook(
        () =>
          useGetExportHistory(1, 10, TEST_PROJECT_KEY, {
            searchText: "export",
            startDate: "2024-01-01",
            endDate: "2024-01-31",
          }),
        { wrapper: createWrapper() },
      );

      await waitFor(() =>
        expect(languageManagerService.getExportHistory).toHaveBeenCalledWith(
          expect.objectContaining({
            filters: {
              searchText: "export",
              startDate: "2024-01-01",
              endDate: "2024-01-31",
            },
          }),
        ),
      );
    });
  });

  // ─── useSaveBlocksLanguageKey ─────────────────────────────────────────────

  describe("useSaveBlocksLanguageKey", () => {
    it("should save a language key successfully", async () => {
      vi.mocked(languageManagerService.saveBlocksLanguageKey).mockResolvedValue(
        mockSuccessResponse,
      );

      const { result } = renderHook(() => useSaveBlocksLanguageKey(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveLanguageKeyPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(languageManagerService.saveBlocksLanguageKey).toHaveBeenCalledWith(
        mockSaveLanguageKeyPayload,
      );
    });

    it("should invalidate get-blocksLanguageKeys, get-blocksLanguageKey and get-uilm-timeline on success", async () => {
      vi.mocked(languageManagerService.saveBlocksLanguageKey).mockResolvedValue(
        mockSuccessResponse,
      );
      vi.mocked(languageManagerService.fetchBlocksLanguageKey).mockResolvedValue(
        mockLanguageKeysResponse,
      );
      vi.mocked(languageManagerService.getKeysTimeline).mockResolvedValue(mockGetTimelineResponse);

      const wrapper = createWrapper();

      const { result: keysResult } = renderHook(
        () => useGetBlocksLanguageKey(1, 10, "", [], false),
        { wrapper },
      );
      const { result: timelineResult } = renderHook(
        () => useGetLanguageKeysTimeline(1, 10, "key-1"),
        { wrapper },
      );
      await waitFor(() => {
        expect(keysResult.current.isSuccess).toBe(true);
        expect(timelineResult.current.isSuccess).toBe(true);
      });

      const { result: saveResult } = renderHook(() => useSaveBlocksLanguageKey(), { wrapper });
      saveResult.current.mutate(mockSaveLanguageKeyPayload);

      await waitFor(() => expect(saveResult.current.isSuccess).toBe(true));

      await waitFor(() => {
        expect(languageManagerService.fetchBlocksLanguageKey).toHaveBeenCalledTimes(2);
        expect(languageManagerService.getKeysTimeline).toHaveBeenCalledTimes(2);
      });
    });

    it("should handle errors", async () => {
      vi.mocked(languageManagerService.saveBlocksLanguageKey).mockRejectedValue(
        new Error("Save failed"),
      );

      const { result } = renderHook(() => useSaveBlocksLanguageKey(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveLanguageKeyPayload);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useSaveLanguageModule ────────────────────────────────────────────────

  describe("useSaveLanguageModule", () => {
    it("should save a language module successfully", async () => {
      vi.mocked(languageManagerService.saveLanguageModule).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useSaveLanguageModule(), { wrapper: createWrapper() });

      result.current.mutate(mockSaveLanguageModulePayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(languageManagerService.saveLanguageModule).toHaveBeenCalledWith(
        mockSaveLanguageModulePayload,
      );
    });

    it("should invalidate get-language-modules on success", async () => {
      vi.mocked(languageManagerService.saveLanguageModule).mockResolvedValue(mockSuccessResponse);
      vi.mocked(languageManagerService.fetchBlocksLanguageModules).mockResolvedValue(
        mockLanguageModuleList,
      );

      const wrapper = createWrapper();

      const { result: modulesResult } = renderHook(() => useGetLanguageModules(), { wrapper });
      await waitFor(() => expect(modulesResult.current.isSuccess).toBe(true));

      const { result: saveResult } = renderHook(() => useSaveLanguageModule(), { wrapper });
      saveResult.current.mutate(mockSaveLanguageModulePayload);

      await waitFor(() => expect(saveResult.current.isSuccess).toBe(true));

      await waitFor(() => {
        expect(languageManagerService.fetchBlocksLanguageModules).toHaveBeenCalledTimes(2);
      });
    });
  });

  // ─── useSaveLanguage ──────────────────────────────────────────────────────

  describe("useSaveLanguage", () => {
    it("should save a language successfully", async () => {
      vi.mocked(languageManagerService.saveLanguage).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useSaveLanguage(), { wrapper: createWrapper() });

      result.current.mutate(mockSaveLanguagePayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(languageManagerService.saveLanguage).toHaveBeenCalledWith(mockSaveLanguagePayload);
    });

    it("should invalidate get-languages on success", async () => {
      vi.mocked(languageManagerService.saveLanguage).mockResolvedValue(mockSuccessResponse);
      vi.mocked(languageManagerService.fetchBlocksLanguages).mockResolvedValue(
        mockLanguageConfigList,
      );

      const wrapper = createWrapper();

      const { result: languagesResult } = renderHook(() => useGetLanguages(), { wrapper });
      await waitFor(() => expect(languagesResult.current.isSuccess).toBe(true));

      const { result: saveResult } = renderHook(() => useSaveLanguage(), { wrapper });
      saveResult.current.mutate(mockSaveLanguagePayload);

      await waitFor(() => expect(saveResult.current.isSuccess).toBe(true));

      await waitFor(() => {
        expect(languageManagerService.fetchBlocksLanguages).toHaveBeenCalledTimes(2);
      });
    });
  });

  // ─── useDeleteLanguageKey ─────────────────────────────────────────────────

  describe("useDeleteLanguageKey", () => {
    it("should delete a language key successfully", async () => {
      vi.mocked(languageManagerService.deleteLanguageKey).mockResolvedValue(
        mockDeleteSuccessResponse,
      );

      const { result } = renderHook(() => useDeleteLanguageKey(), { wrapper: createWrapper() });

      result.current.mutate(mockDeleteLanguageKeyPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(languageManagerService.deleteLanguageKey).toHaveBeenCalledWith(
        mockDeleteLanguageKeyPayload,
      );
    });

    it("should invalidate get-blocksLanguageKeys on success", async () => {
      vi.mocked(languageManagerService.deleteLanguageKey).mockResolvedValue(
        mockDeleteSuccessResponse,
      );
      vi.mocked(languageManagerService.fetchBlocksLanguageKey).mockResolvedValue(
        mockLanguageKeysResponse,
      );

      const wrapper = createWrapper();

      const { result: keysResult } = renderHook(
        () => useGetBlocksLanguageKey(1, 10, "", [], false),
        { wrapper },
      );
      await waitFor(() => expect(keysResult.current.isSuccess).toBe(true));

      const { result: deleteResult } = renderHook(() => useDeleteLanguageKey(), { wrapper });
      deleteResult.current.mutate(mockDeleteLanguageKeyPayload);

      await waitFor(() => expect(deleteResult.current.isSuccess).toBe(true));

      await waitFor(() => {
        expect(languageManagerService.fetchBlocksLanguageKey).toHaveBeenCalledTimes(2);
      });
    });

    it("should handle errors", async () => {
      vi.mocked(languageManagerService.deleteLanguageKey).mockRejectedValue(
        new Error("Delete failed"),
      );

      const { result } = renderHook(() => useDeleteLanguageKey(), { wrapper: createWrapper() });

      result.current.mutate(mockDeleteLanguageKeyPayload);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useDeleteLanguage ────────────────────────────────────────────────────

  describe("useDeleteLanguage", () => {
    it("should delete a language successfully", async () => {
      vi.mocked(languageManagerService.deleteLanguage).mockResolvedValue(mockDeleteSuccessResponse);

      const { result } = renderHook(() => useDeleteLanguage(), { wrapper: createWrapper() });

      result.current.mutate(mockDeleteLanguagePayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(languageManagerService.deleteLanguage).toHaveBeenCalledWith(mockDeleteLanguagePayload);
    });

    it("should invalidate get-languages on success", async () => {
      vi.mocked(languageManagerService.deleteLanguage).mockResolvedValue(mockDeleteSuccessResponse);
      vi.mocked(languageManagerService.fetchBlocksLanguages).mockResolvedValue(
        mockLanguageConfigList,
      );

      const wrapper = createWrapper();

      const { result: languagesResult } = renderHook(() => useGetLanguages(), { wrapper });
      await waitFor(() => expect(languagesResult.current.isSuccess).toBe(true));

      const { result: deleteResult } = renderHook(() => useDeleteLanguage(), { wrapper });
      deleteResult.current.mutate(mockDeleteLanguagePayload);

      await waitFor(() => expect(deleteResult.current.isSuccess).toBe(true));

      await waitFor(() => {
        expect(languageManagerService.fetchBlocksLanguages).toHaveBeenCalledTimes(2);
      });
    });
  });

  // ─── useSetDefaultLanguage ────────────────────────────────────────────────

  describe("useSetDefaultLanguage", () => {
    it("should set default language successfully", async () => {
      vi.mocked(languageManagerService.setDefault).mockResolvedValue(mockDeleteSuccessResponse);

      const { result } = renderHook(() => useSetDefaultLanguage(), { wrapper: createWrapper() });

      result.current.mutate(mockSetDefaultPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(languageManagerService.setDefault).toHaveBeenCalledWith(mockSetDefaultPayload);
    });

    it("should invalidate get-languages on success", async () => {
      vi.mocked(languageManagerService.setDefault).mockResolvedValue(mockDeleteSuccessResponse);
      vi.mocked(languageManagerService.fetchBlocksLanguages).mockResolvedValue(
        mockLanguageConfigList,
      );

      const wrapper = createWrapper();

      const { result: languagesResult } = renderHook(() => useGetLanguages(), { wrapper });
      await waitFor(() => expect(languagesResult.current.isSuccess).toBe(true));

      const { result: setDefaultResult } = renderHook(() => useSetDefaultLanguage(), { wrapper });
      setDefaultResult.current.mutate(mockSetDefaultPayload);

      await waitFor(() => expect(setDefaultResult.current.isSuccess).toBe(true));

      await waitFor(() => {
        expect(languageManagerService.fetchBlocksLanguages).toHaveBeenCalledTimes(2);
      });
    });
  });

  // ─── useTranslateAll ──────────────────────────────────────────────────────

  describe("useTranslateAll", () => {
    it("should trigger translateAll mutation successfully", async () => {
      vi.mocked(languageManagerService.translateAll).mockResolvedValue({
        errors: null,
        isSuccess: true,
      });

      const { result } = renderHook(() => useTranslateAll(), { wrapper: createWrapper() });

      result.current.mutate(mockTranslateAllPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(languageManagerService.translateAll).toHaveBeenCalledWith(mockTranslateAllPayload);
    });

    it("should invalidate get-blocksLanguageKeys and get-blocksLanguageKey on success", async () => {
      vi.mocked(languageManagerService.translateAll).mockResolvedValue({
        errors: null,
        isSuccess: true,
      });
      vi.mocked(languageManagerService.fetchBlocksLanguageKey).mockResolvedValue(
        mockLanguageKeysResponse,
      );

      const wrapper = createWrapper();

      const { result: keysResult } = renderHook(
        () => useGetBlocksLanguageKey(1, 10, "", [], false),
        { wrapper },
      );
      await waitFor(() => expect(keysResult.current.isSuccess).toBe(true));

      const { result: translateResult } = renderHook(() => useTranslateAll(), { wrapper });
      translateResult.current.mutate(mockTranslateAllPayload);

      await waitFor(() => expect(translateResult.current.isSuccess).toBe(true));

      await waitFor(() => {
        expect(languageManagerService.fetchBlocksLanguageKey).toHaveBeenCalledTimes(2);
      });
    });
  });

  // ─── useTranslateKey ──────────────────────────────────────────────────────

  describe("useTranslateKey", () => {
    it("should trigger translateKey mutation successfully", async () => {
      vi.mocked(languageManagerService.translateKey).mockResolvedValue({
        errors: null,
        isSuccess: true,
      });

      const { result } = renderHook(() => useTranslateKey(), { wrapper: createWrapper() });

      result.current.mutate(mockTranslateKeyPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(languageManagerService.translateKey).toHaveBeenCalledWith(mockTranslateKeyPayload);
    });
  });

  // ─── useGenerateUilmFile ──────────────────────────────────────────────────

  describe("useGenerateUilmFile", () => {
    it("should generate UILM file successfully with no query invalidation", async () => {
      vi.mocked(languageManagerService.generateUilmFile).mockResolvedValue({
        errors: null,
        isSuccess: true,
      });

      const { result } = renderHook(() => useGenerateUilmFile(), { wrapper: createWrapper() });

      result.current.mutate(mockGenerateUilmFilePayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(languageManagerService.generateUilmFile).toHaveBeenCalledWith(
        mockGenerateUilmFilePayload,
      );
    });
  });

  // ─── useGetTranslationSuggestion ──────────────────────────────────────────

  describe("useGetTranslationSuggestion", () => {
    it("should get a translation suggestion successfully", async () => {
      vi.mocked(languageManagerService.getTranslationSuggestion).mockResolvedValue(
        mockTranslationSuggestionResponse,
      );

      const { result } = renderHook(() => useGetTranslationSuggestion(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockTranslationSuggestionPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockTranslationSuggestionResponse);
      expect(languageManagerService.getTranslationSuggestion).toHaveBeenCalledWith(
        mockTranslationSuggestionPayload,
      );
    });

    it("should handle errors gracefully", async () => {
      vi.mocked(languageManagerService.getTranslationSuggestion).mockRejectedValue(
        new Error("AI service unavailable"),
      );

      const { result } = renderHook(() => useGetTranslationSuggestion(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockTranslationSuggestionPayload);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useImportLanguageFile ────────────────────────────────────────────────

  describe("useImportLanguageFile", () => {
    it("should import a language file successfully", async () => {
      vi.mocked(languageManagerService.importLanguageFile).mockResolvedValue(mockSuccessResponse);

      const { result } = renderHook(() => useImportLanguageFile(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockImportFile);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(languageManagerService.importLanguageFile).toHaveBeenCalledWith(mockImportFile);
    });
  });

  // ─── useSaveLanguageKeyUilmExport ─────────────────────────────────────────

  describe("useSaveLanguageKeyUilmExport", () => {
    it("should export language keys successfully", async () => {
      vi.mocked(languageManagerService.saveLanguageKeyUilmExport).mockResolvedValue(
        mockSuccessResponse,
      );

      const { result } = renderHook(() => useSaveLanguageKeyUilmExport(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockKeyUilmExport);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(languageManagerService.saveLanguageKeyUilmExport).toHaveBeenCalledWith(
        mockKeyUilmExport,
      );
    });
  });

  // ─── useRevertKeyTimeline ─────────────────────────────────────────────────

  describe("useRevertKeyTimeline", () => {
    it("should revert a key timeline successfully", async () => {
      vi.mocked(languageManagerService.revertKeyTimeline).mockResolvedValue(mockRollbackResponse);

      const { result } = renderHook(() => useRevertKeyTimeline(), { wrapper: createWrapper() });

      result.current.mutate(mockRevertKeyTimelinePayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(languageManagerService.revertKeyTimeline).toHaveBeenCalledWith(
        mockRevertKeyTimelinePayload,
      );
    });

    it("should invalidate get-uilm-timeline on success", async () => {
      vi.mocked(languageManagerService.revertKeyTimeline).mockResolvedValue(mockRollbackResponse);
      vi.mocked(languageManagerService.getKeysTimeline).mockResolvedValue(mockGetTimelineResponse);

      const wrapper = createWrapper();

      const { result: timelineResult } = renderHook(
        () => useGetLanguageKeysTimeline(1, 10, "key-1"),
        { wrapper },
      );
      await waitFor(() => expect(timelineResult.current.isSuccess).toBe(true));

      const { result: revertResult } = renderHook(() => useRevertKeyTimeline(), { wrapper });
      revertResult.current.mutate(mockRevertKeyTimelinePayload);

      await waitFor(() => expect(revertResult.current.isSuccess).toBe(true));

      await waitFor(() => {
        expect(languageManagerService.getKeysTimeline).toHaveBeenCalledTimes(2);
      });
    });
  });
});
