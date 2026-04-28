import { useProjectStore } from "@/store/useProjectStore";
import { ExportHistoryFilters, IKeyUilmExport } from "@blocks-localization/models/language";
import { languageManagerService } from "@blocks-localization/services/language.manager.service";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export const useGetBlocksLanguageKey = (
  pageNumber: number,
  pageSize: number,
  searchKey: string,
  moduleIds: string[],
  isPartiallyTranslated: boolean,
  sortProperty = "",
  isDescending = false,
  createDateRange?: { startDate: string; endDate: string },
  lastUpdateDateRange?: { startDate: string; endDate: string },
  resourceSearchFilters?: { culture: string; searchText: string }[],
) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  return useQuery({
    queryKey: [
      "get-blocksLanguageKeys",
      tenantId,
      pageNumber,
      pageSize,
      searchKey,
      JSON.stringify(moduleIds),
      isPartiallyTranslated,
      sortProperty,
      isDescending,
      createDateRange?.startDate ?? "",
      createDateRange?.endDate ?? "",
      lastUpdateDateRange?.startDate ?? "",
      lastUpdateDateRange?.endDate ?? "",
      JSON.stringify(resourceSearchFilters ?? []),
    ],
    queryFn: () =>
      languageManagerService.fetchBlocksLanguageKey({
        projectKey: tenantId,
        pageNumber: pageNumber,
        pageSize: pageSize,
        searchKey: searchKey,
        moduleIds: moduleIds,
        isPartiallyTranslated: isPartiallyTranslated,
        sortProperty: sortProperty,
        isDescending: isDescending,
        createDateRange: createDateRange,
        lastUpdateDateRange: lastUpdateDateRange,
        resourceSearchFilters: resourceSearchFilters,
      }),
    staleTime: 0,
    refetchOnMount: true,
  });
};

export const useGetBlocksLanguageKeyById = (itemId: string) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  return useQuery({
    queryKey: ["get-blocksLanguageKey", tenantId, itemId],
    queryFn: () =>
      languageManagerService.fetchBlocksLanguageKeyById({
        projectKey: tenantId,
        itemId: itemId,
      }),
    refetchInterval: (query) => {
      const data = query.state.data;
      const hasTranslations = data?.resources?.some(
        (r) => r.culture !== "en-US" && r.value !== "" && r.value !== null,
      );
      if (hasTranslations) return false; // stop polling — translations are ready
      return 5 * 1000; // keep polling
    },
  });
};

export const useGetLanguageModules = () => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  return useQuery({
    queryKey: ["get-language-modules", tenantId],
    queryFn: () => languageManagerService.fetchBlocksLanguageModules(tenantId),
  });
};

export const useGetLanguages = () => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  return useQuery({
    queryKey: ["get-languages", tenantId],
    queryFn: () => languageManagerService.fetchBlocksLanguages(tenantId),
  });
};

export const useSaveBlocksLanguageKey = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["add-blocksLanguageKey"],
    mutationFn: languageManagerService.saveBlocksLanguageKey,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["get-blocksLanguageKey"] });
      queryClient.invalidateQueries({ queryKey: ["get-blocksLanguageKeys"] });
      queryClient.invalidateQueries({ queryKey: ["get-uilm-timeline"] });
      queryClient.invalidateQueries({ queryKey: ["get-localization-timeline"] });
    },
  });
};

export const useSaveLanguageModule = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["add-language-module"],
    mutationFn: languageManagerService.saveLanguageModule,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["get-language-modules"] });
    },
  });
};

export function useGetLanguageModule(projectKey: string) {
  return useQuery({
    queryKey: ["language-modules", projectKey],
    queryFn: () => languageManagerService.getLanguageModule(projectKey),
    enabled: !!projectKey,
  });
}

export const useTranslateAll = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["translate-all"],
    mutationFn: languageManagerService.translateAll,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["get-blocksLanguageKey"] });
      queryClient.invalidateQueries({ queryKey: ["get-blocksLanguageKeys"] });
    },
  });
};

export const useTranslateKey = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["translate-key"],
    mutationFn: languageManagerService.translateKey,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["get-blocksLanguageKey"] });
      queryClient.invalidateQueries({ queryKey: ["get-blocksLanguageKeys"] });
      queryClient.invalidateQueries({ queryKey: ["get-uilm-timeline"] });
      queryClient.invalidateQueries({ queryKey: ["get-localization-timeline"] });
    },
  });
};

export const useSaveLanguage = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["add-language"],
    mutationFn: languageManagerService.saveLanguage,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["get-languages"] });
    },
  });
};

export const useDeleteLanguageKey = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["language-key", "delete"],
    mutationFn: languageManagerService.deleteLanguageKey,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["get-blocksLanguageKeys"] });
    },
  });
};

export const useDeleteLanguage = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["language-config", "delete"],
    mutationFn: languageManagerService.deleteLanguage,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["get-languages"] });
    },
  });
};

export const useSetDefaultLanguage = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["language-config", "set-default"],
    mutationFn: languageManagerService.setDefault,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["get-languages"] });
    },
  });
};

export const useGenerateUilmFile = () => {
  return useMutation({
    mutationKey: ["add-language"],
    mutationFn: languageManagerService.generateUilmFile,
    onSuccess: () => {},
  });
};

export const useGetTranslationSuggestion = () => {
  return useMutation({
    mutationKey: ["get-translation-suggestion"],
    mutationFn: languageManagerService.getTranslationSuggestion,
    onSuccess: () => {},
  });
};

export const useImportLanguageFile = () => {
  return useMutation({
    mutationKey: ["import-language-file"],
    mutationFn: languageManagerService.importLanguageFile,
    onSuccess: () => {},
  });
};

export const useSaveLanguageKeyUilmExport = () => {
  return useMutation({
    mutationKey: ["save-language-key-uilm-export"],
    mutationFn: (payload: IKeyUilmExport) =>
      languageManagerService.saveLanguageKeyUilmExport(payload),
    onSuccess: () => {},
  });
};

export const useGetLanguageKeysTimeline = (pageNumber: number, pageSize: number, keyId: string) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  return useQuery({
    queryKey: ["get-uilm-timeline", tenantId, pageNumber, pageSize, keyId],
    queryFn: () =>
      languageManagerService.getKeysTimeline({
        projectKey: tenantId,
        pageNumber: pageNumber,
        pageSize: pageSize,
        keyId,
      }),
  });
};

export const useGetExportHistory = (
  pageNumber: number,
  pageSize: number,
  projectKey: string,
  filters: ExportHistoryFilters,
) => {
  return useQuery({
    queryKey: [
      "export-history",
      projectKey,
      pageNumber,
      pageSize,
      filters?.searchText ?? "",
      filters?.startDate ?? "",
      filters?.endDate ?? "",
    ],
    queryFn: () =>
      languageManagerService.getExportHistory({
        projectKey,
        pageNumber,
        pageSize,
        filters,
      }),
  });
};

export const useRevertKeyTimeline = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["revert-uilm-key-timeline"],
    mutationFn: (payload: { itemId: string; projectKey: string }) =>
      languageManagerService.revertKeyTimeline(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["get-uilm-timeline"] });
      queryClient.invalidateQueries({ queryKey: ["get-localization-timeline"] });
    },
  });
};

export const useGetLocalizationTimeline = (
  pageNumber: number,
  pageSize: number,
  filters?: {
    userId?: string;
    logFrom?: string;
    logFromValues?: string[];
    excludeLogFromValues?: string[];
    createDateRange?: { startDate?: string; endDate?: string };
  },
) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  return useQuery({
    queryKey: [
      "get-localization-timeline",
      tenantId,
      pageNumber,
      pageSize,
      filters?.userId ?? "",
      filters?.logFrom ?? "",
      filters?.logFromValues?.join(",") ?? "",
      filters?.excludeLogFromValues?.join(",") ?? "",
      filters?.createDateRange?.startDate ?? "",
      filters?.createDateRange?.endDate ?? "",
    ],
    queryFn: () =>
      languageManagerService.getLocalizationTimeline({
        projectKey: tenantId,
        pageNumber,
        pageSize,
        userId: filters?.userId,
        logFrom: filters?.logFrom,
        logFromValues: filters?.logFromValues,
        excludeLogFromValues: filters?.excludeLogFromValues,
        createDateRange: filters?.createDateRange,
      }),
  });
};

export const useGetTimelineByOperationId = (
  operationId: string,
  pageNumber: number,
  pageSize: number,
) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  return useQuery({
    queryKey: ["get-timeline-by-operation", tenantId, operationId, pageNumber, pageSize],
    queryFn: () =>
      languageManagerService.getTimelineByOperationId({
        operationId,
        projectKey: tenantId,
        pageNumber,
        pageSize,
      }),
    enabled: !!operationId,
  });
};
