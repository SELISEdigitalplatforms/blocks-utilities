import { SERVICES } from "./services.constant";

export enum TRACE_PROVIDERS {
  hot = "hot",
  cold = "cold",
  archive = "archive",
}

export const CLOUD_BUILTIN_SERVICES = SERVICES.filter((s) => s.showInTraces).map((s) => ({
  label: s.label,
  value: `blocks-${s.serviceName}-api` as const,
}));
