import { http } from "@/lib/http-client";
import {
  LANGUAGE_ASSISTANT_ENDPOINTS,
  LANGUAGE_ENDPOINTS,
  LANGUAGE_KEY_ENDPOINTS,
  LANGUAGE_MODULE_ENDPOINTS,
} from "@blocks-localization/constants/endpoint.constant";
import {
  ExportHistoryFilters,
  IBlocksLanguageKey,
  IGetExportHistory,
  IGetLocalizationTimelineResponse,
  IGetTimelineByOperationIdResponse,
  IGetTimelineResponse,
  IImportFile,
  IKeyUilmExport,
  ILanguageConfig,
  ILanguageModule,
  IModuleGets,
  IRollbackResponse,
  IValidationError,
} from "@blocks-localization/models/language";

class LanguageManagerService {
  fetchBlocksLanguageKey = (request: {
    projectKey: string;
    pageNumber: number;
    pageSize: number;
    searchKey: string;
    moduleIds: string[];
    isPartiallyTranslated: boolean;
    sortProperty: string;
    isDescending: boolean;
    createDateRange?: {
      startDate?: string;
      endDate?: string;
    };
    lastUpdateDateRange?: {
      startDate?: string;
      endDate?: string;
    };
    resourceSearchFilters?: { culture: string; searchText: string }[];
  }): Promise<{ totalCount: number; keys: IBlocksLanguageKey[] }> => {
    const url = LANGUAGE_KEY_ENDPOINTS.GETS;
    const payload = { ...request };
    if (!request.createDateRange) {
      delete payload.createDateRange;
    } else if (payload.createDateRange?.startDate === "") {
      delete payload.createDateRange.startDate;
    } else if (payload?.createDateRange?.endDate === "") {
      delete payload.createDateRange.endDate;
    }
    if (!request.lastUpdateDateRange) {
      delete payload.lastUpdateDateRange;
    } else if (payload.lastUpdateDateRange?.startDate === "") {
      delete payload.lastUpdateDateRange.startDate;
    } else if (payload?.lastUpdateDateRange?.endDate === "") {
      delete payload.lastUpdateDateRange.endDate;
    }
    return http.post(url, payload);
  };

  fetchBlocksLanguageKeyById = (request: {
    projectKey: string;
    itemId: string;
  }): Promise<IBlocksLanguageKey> => {
    return http.get(
      `${LANGUAGE_KEY_ENDPOINTS.GET}?projectKey=${request.projectKey}&itemId=${request.itemId}`,
    );
  };

  fetchBlocksLanguageModules = (projectKey: string): Promise<ILanguageModule[]> => {
    return http.get(`${LANGUAGE_MODULE_ENDPOINTS.GETS}?projectKey=${projectKey}`);
  };

  fetchBlocksLanguages = (projectKey: string): Promise<ILanguageConfig[]> => {
    return http.get(`${LANGUAGE_ENDPOINTS.GETS}?projectKey=${projectKey}`);
  };

  saveBlocksLanguageKey = (payload: {
    itemId: string;
    keyName: string;
    moduleId: string;
    resources: {
      value: string;
      culture: string;
    }[];
    routes: string[];
    isPartiallyTranslated: boolean;
    projectKey: string;
    isNewKey?: boolean;
    context?: string;
  }): Promise<{
    success: boolean;
    errorMessage: string;
    validationErrors: IValidationError[];
  }> => {
    const url = LANGUAGE_KEY_ENDPOINTS.SAVE;
    const updatedPayload = { ...payload, isNewKey: payload.isNewKey ?? false };
    return http.post(url, updatedPayload);
  };

  saveLanguageModule = (payload: {
    moduleName: string;
    projectKey: string;
  }): Promise<{
    errorMessage: null | unknown;
    success: boolean;
    validationErrors: IValidationError[] | null;
  }> => {
    const url = LANGUAGE_MODULE_ENDPOINTS.SAVE;
    return http.post(url, payload);
  };

  getLanguageModule = (ProjectKey: string): Promise<IModuleGets[]> => {
    const url = `${LANGUAGE_MODULE_ENDPOINTS.GETS}?ProjectKey=${ProjectKey}`;
    return http.get(url);
  };

  saveLanguage = (payload: {
    languageName: string;
    languageCode: string;
    projectKey: string;
  }): Promise<{
    errorMessage: null | unknown;
    success: boolean;
  }> => {
    const url = LANGUAGE_ENDPOINTS.SAVE;
    return http.post(url, payload);
  };

  deleteLanguageKey(payload: { itemId: string; projectKey: string }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> {
    const url = LANGUAGE_KEY_ENDPOINTS.DELETE;
    return http
      .delete<{
        errors: unknown;
        isSuccess: boolean;
      }>(`${url}?itemId=${payload.itemId}&projectKey=${payload.projectKey}`)
      .then((response) => response);
  }

  deleteLanguage(payload: { languageName: string; projectKey: string }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> {
    const url = LANGUAGE_ENDPOINTS.DELETE;
    return http
      .delete<{
        errors: unknown;
        isSuccess: boolean;
      }>(`${url}?languageName=${payload.languageName}&projectKey=${payload.projectKey}`)
      .then((response) => response);
  }

  setDefault = (payload: {
    languageName: string;
    projectKey: string;
  }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> => {
    const url = LANGUAGE_ENDPOINTS.SET_DEFAULT;
    return http.post(url, payload);
  };

  generateUilmFile = (payload: {
    guid: string;
    projectKey: string;
  }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> => {
    const url = LANGUAGE_KEY_ENDPOINTS.GENERATE_UILM_FILE;
    return http.post(url, payload);
  };

  getTranslationSuggestion = (payload: {
    sourceText: string;
    destinationLanguage: string;
    currentLanguage: string;
    temperature: number;
    elementDetailContext: string;
  }): Promise<{
    content: string;
    errors: null | unknown;
    isSuccess: boolean;
  }> => {
    const url = LANGUAGE_ASSISTANT_ENDPOINTS.GET_TRANSLATION_SUGGESTION;
    return http.post(url, payload);
  };

  translateAll = (payload: {
    projectKey: string;
    messageCoRelationId: string;
    defaultLanguage: string;
    moduleId?: string;
  }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> => {
    const url = LANGUAGE_KEY_ENDPOINTS.TRANSLATE_ALL;
    return http.post(url, payload);
  };

  translateKey = (payload: {
    keyId: string;
    projectKey: string;
    defaultLanguage: string;
    messageCoRelationId: string;
  }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> => {
    const url = LANGUAGE_KEY_ENDPOINTS.TRANSLATE_KEY;
    return http.post(url, payload);
  };

  importLanguageFile = (payload: IImportFile) => {
    const url = LANGUAGE_KEY_ENDPOINTS.UILM_IMPORT;
    return http.post(url, payload);
  };

  saveLanguageKeyUilmExport = (payload: IKeyUilmExport) => {
    const url = LANGUAGE_KEY_ENDPOINTS.UILM_EXPORT;
    return http.post(url, payload);
  };

  getKeysTimeline = (payload: {
    pageNumber: number;
    pageSize: number;
    keyId: string;
    projectKey: string;
  }): Promise<IGetTimelineResponse> => {
    const url = `${LANGUAGE_KEY_ENDPOINTS.GET_TIMELINE}?pageSize=${payload.pageSize}&pageNumber=${payload.pageNumber}&projectKey=${payload.projectKey}&EntityId=${payload.keyId}`;

    return http.get(url);
  };

  getExportHistory = (payload: {
    projectKey: string;
    pageNumber: number;
    pageSize: number;
    filters: ExportHistoryFilters;
  }): Promise<IGetExportHistory> => {
    const { projectKey, pageNumber, pageSize, filters } = payload;

    const params = new URLSearchParams({
      PageSize: String(pageSize),
      PageNumber: String(pageNumber),
      ProjectKey: projectKey,
    });

    if (filters?.searchText) {
      params.append("SearchText", filters.searchText);
    }
    if (filters?.startDate) {
      params.append("CreateDateRange.StartDate", filters.startDate);
    }
    if (filters?.endDate) {
      params.append("CreateDateRange.EndDate", filters.endDate);
    }

    const url = `${LANGUAGE_KEY_ENDPOINTS.GET_EXPORT_HISTORY}?${params.toString()}`;
    return http.get(url);
  };

  revertKeyTimeline = (payload: {
    itemId: string;
    projectKey: string;
  }): Promise<IRollbackResponse> => {
    const url = LANGUAGE_KEY_ENDPOINTS.ROLLBACK;

    return http.post(url, payload);
  };

  getLocalizationTimeline = (payload: {
    projectKey: string;
    pageNumber: number;
    pageSize: number;
    userId?: string;
    logFrom?: string;
    logFromValues?: string[];
    excludeLogFromValues?: string[];
    createDateRange?: { startDate?: string; endDate?: string };
  }): Promise<IGetLocalizationTimelineResponse> => {
    const params = new URLSearchParams({
      PageSize: String(payload.pageSize),
      PageNumber: String(payload.pageNumber),
      ProjectKey: payload.projectKey,
    });

    if (payload.userId) {
      params.append("UserId", payload.userId);
    }
    if (payload.logFrom) {
      params.append("LogFrom", payload.logFrom);
    }
    if (payload.logFromValues) {
      payload.logFromValues.forEach((v) => params.append("LogFromValues", v));
    }
    if (payload.excludeLogFromValues) {
      payload.excludeLogFromValues.forEach((v) => params.append("ExcludeLogFromValues", v));
    }
    if (payload.createDateRange?.startDate) {
      params.append("CreateDateRange.StartDate", payload.createDateRange.startDate);
    }
    if (payload.createDateRange?.endDate) {
      params.append("CreateDateRange.EndDate", payload.createDateRange.endDate);
    }

    const url = `${LANGUAGE_KEY_ENDPOINTS.GET_LOCALIZATION_TIMELINE}?${params.toString()}`;
    return http.get(url);
  };

  getTimelineByOperationId = (payload: {
    operationId: string;
    projectKey: string;
    pageNumber: number;
    pageSize: number;
  }): Promise<IGetTimelineByOperationIdResponse> => {
    const params = new URLSearchParams({
      OperationId: payload.operationId,
      PageSize: String(payload.pageSize),
      PageNumber: String(payload.pageNumber),
      ProjectKey: payload.projectKey,
    });

    const url = `${LANGUAGE_KEY_ENDPOINTS.GET_TIMELINE_BY_OPERATION_ID}?${params.toString()}`;
    return http.get(url);
  };
}
export const languageManagerService = new LanguageManagerService();
