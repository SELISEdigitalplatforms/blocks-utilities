import { API_BASES } from "@/constants/endpoint.constant";

// ─── Log endpoints ────────────────────────────────────────────────────────────

const LOG_SUBPATH = "/Log";

export const LOG_ENDPOINTS = {
  GET_LOGS: `${API_BASES.LMT}${LOG_SUBPATH}/GetLogs`,
  GET_LOGS_BY_DATE: `${API_BASES.LMT}${LOG_SUBPATH}/GetLogsByDate`,
  LIVE: `${API_BASES.LMT}${LOG_SUBPATH}/Live`,
} as const;

// ─── Trace endpoints ──────────────────────────────────────────────────────────

const TRACE_SUBPATH = "/Trace";

export const TRACE_ENDPOINTS = {
  GET_TRACES: `${API_BASES.LMT}${TRACE_SUBPATH}/GetTraces`,
  GET_TRACE: `${API_BASES.LMT}${TRACE_SUBPATH}/GetTrace`,
  GET_OPERATIONAL_ANALYTICS: `${API_BASES.LMT}${TRACE_SUBPATH}/GetOperationalAnalytics`,
  GET_SERVICE_ANALYTICS: `${API_BASES.LMT}${TRACE_SUBPATH}/GetServiceAnalytics`,
} as const;
