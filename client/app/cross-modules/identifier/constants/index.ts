import { tStatic } from "@seliseblocks/genesis-os";

export enum REGISTER_SERVICE_TYPE {
  None = 0,
  Api = 1,
  Worker = 2,
}

export const getRegisterServiceTypes = (): { value: REGISTER_SERVICE_TYPE; label: string }[] => [
  { value: REGISTER_SERVICE_TYPE.None, label: tStatic("utilities.console", "CONSTANTS.NONE") },
  { value: REGISTER_SERVICE_TYPE.Api, label: "API" },
  { value: REGISTER_SERVICE_TYPE.Worker, label: tStatic("utilities.console", "CONSTANTS.WORKER") },
];

export const getRegisterServiceEnvironments = () => [
  { value: "prod", label: tStatic("utilities.console", "CONSTANTS.PRODUCTION") },
  { value: "stg", label: tStatic("utilities.console", "CONSTANTS.STAGING") },
  { value: "dev", label: tStatic("utilities.console", "CONSTANTS.DEVELOPMENT") },
];

export const getLogLevels = () => [
  { value: "debug", label: tStatic("utilities.console", "CONSTANTS.DEBUG"), color: "bg-gray-500" },
  { value: "info", label: tStatic("utilities.console", "CONSTANTS.INFO"), color: "bg-blue-500" },
  { value: "warn", label: tStatic("utilities.console", "CONSTANTS.WARNING"), color: "bg-yellow-500" },
  { value: "error", label: tStatic("utilities.console", "common:ERROR"), color: "bg-red-500" },
  { value: "fatal", label: tStatic("utilities.console", "CONSTANTS.FATAL"), color: "bg-red-700" },
] as const;

export const getServiceStatus = () => [
  { value: "active", label: tStatic("utilities.console", "CONSTANTS.ACTIVE"), color: "bg-green-500" },
  { value: "inactive", label: tStatic("utilities.console", "CONSTANTS.INACTIVE"), color: "bg-gray-500" },
  { value: "error", label: tStatic("utilities.console", "common:ERROR"), color: "bg-red-500" },
] as const;

export const getTraceStatus = () => [
  { value: "ok", label: tStatic("utilities.console", "common:SUCCESS"), color: "bg-green-500" },
  { value: "error", label: tStatic("utilities.console", "common:ERROR"), color: "bg-red-500" },
  { value: "timeout", label: tStatic("utilities.console", "CONSTANTS.TIMEOUT"), color: "bg-yellow-500" },
] as const;
