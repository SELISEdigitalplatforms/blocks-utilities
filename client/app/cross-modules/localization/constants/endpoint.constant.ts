import { API_BASES } from "@/constants/endpoint.constant";

const KEY_SUBPATH = "/Key";
const MODULE_SUBPATH = "/Module";
const LANGUAGE_SUBPATH = "/Language";
const ASSISTANT_SUBPATH = "/Assistant";

// Language Key endpoints
export const LANGUAGE_KEY_ENDPOINTS = {
  GETS: `${API_BASES.UILM}${KEY_SUBPATH}/Gets`,
  GET: `${API_BASES.UILM}${KEY_SUBPATH}/Get`,
  SAVE: `${API_BASES.UILM}${KEY_SUBPATH}/Save`,
  DELETE: `${API_BASES.UILM}${KEY_SUBPATH}/Delete`,
  GENERATE_UILM_FILE: `${API_BASES.UILM}${KEY_SUBPATH}/GenerateUilmFile`,
  TRANSLATE_ALL: `${API_BASES.UILM}${KEY_SUBPATH}/TranslateAll`,
  TRANSLATE_KEY: `${API_BASES.UILM}${KEY_SUBPATH}/TranslateKey`,
  UILM_IMPORT: `${API_BASES.UILM}${KEY_SUBPATH}/UilmImport`,
  UILM_EXPORT: `${API_BASES.UILM}${KEY_SUBPATH}/UilmExport`,
  GET_TIMELINE: `${API_BASES.UILM}${KEY_SUBPATH}/GetTimeline`,
  GET_EXPORT_HISTORY: `${API_BASES.UILM}${KEY_SUBPATH}/GetUilmExportedFiles`,
  ROLLBACK: `${API_BASES.UILM}${KEY_SUBPATH}/RollBack`,
  GET_LOCALIZATION_TIMELINE: `${API_BASES.UILM}${KEY_SUBPATH}/GetLocalizationTimeline`,
  GET_TIMELINE_BY_OPERATION_ID: `${API_BASES.UILM}${KEY_SUBPATH}/GetTimelineByOperationId`,
} as const;

// Language Module endpoints
export const LANGUAGE_MODULE_ENDPOINTS = {
  GETS: `${API_BASES.UILM}${MODULE_SUBPATH}/Gets`,
  SAVE: `${API_BASES.UILM}${MODULE_SUBPATH}/Save`,
} as const;

// Language endpoints
export const LANGUAGE_ENDPOINTS = {
  GETS: `${API_BASES.UILM}${LANGUAGE_SUBPATH}/Gets`,
  SAVE: `${API_BASES.UILM}${LANGUAGE_SUBPATH}/Save`,
  DELETE: `${API_BASES.UILM}${LANGUAGE_SUBPATH}/Delete`,
  SET_DEFAULT: `${API_BASES.UILM}${LANGUAGE_SUBPATH}/SetDefault`,
} as const;

// Language Assistant endpoints
export const LANGUAGE_ASSISTANT_ENDPOINTS = {
  GET_TRANSLATION_SUGGESTION: `${API_BASES.UILM}${ASSISTANT_SUBPATH}/GetTranslationSuggestion`,
} as const;
