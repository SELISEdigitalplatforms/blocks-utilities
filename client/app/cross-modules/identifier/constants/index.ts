import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_CONSOLE_MODULE = "utilities.console";
const COMMON_ERROR = "common:ERROR";
const LOG_ERROR_COLOR = "bg-red-500";

export enum REGISTER_SERVICE_TYPE {
  None = 0,
  Api = 1,
  Worker = 2,
}

export const getRegisterServiceTypes = (): { value: REGISTER_SERVICE_TYPE; label: string }[] => [
  { value: REGISTER_SERVICE_TYPE.None, label: tStatic(UTILITIES_CONSOLE_MODULE, "CONSTANTS.NONE") },
  { value: REGISTER_SERVICE_TYPE.Api, label: "API" },
  { value: REGISTER_SERVICE_TYPE.Worker, label: tStatic(UTILITIES_CONSOLE_MODULE, "CONSTANTS.WORKER") },
];

export const getRegisterServiceEnvironments = () => [
  { value: "prod", label: tStatic(UTILITIES_CONSOLE_MODULE, "CONSTANTS.PRODUCTION") },
  { value: "stg", label: tStatic(UTILITIES_CONSOLE_MODULE, "CONSTANTS.STAGING") },
  { value: "dev", label: tStatic(UTILITIES_CONSOLE_MODULE, "CONSTANTS.DEVELOPMENT") },
];

export const getLogLevels = () => [
  { value: "debug", label: tStatic(UTILITIES_CONSOLE_MODULE, "CONSTANTS.DEBUG"), color: "bg-gray-500" },
  { value: "info", label: tStatic(UTILITIES_CONSOLE_MODULE, "CONSTANTS.INFO"), color: "bg-blue-500" },
  { value: "warn", label: tStatic(UTILITIES_CONSOLE_MODULE, "CONSTANTS.WARNING"), color: "bg-yellow-500" },
  { value: "error", label: tStatic(UTILITIES_CONSOLE_MODULE, COMMON_ERROR), color: LOG_ERROR_COLOR },
  { value: "fatal", label: tStatic(UTILITIES_CONSOLE_MODULE, "CONSTANTS.FATAL"), color: "bg-red-700" },
] as const;

export const getServiceStatus = () => [
  { value: "active", label: tStatic(UTILITIES_CONSOLE_MODULE, "CONSTANTS.ACTIVE"), color: "bg-green-500" },
  { value: "inactive", label: tStatic(UTILITIES_CONSOLE_MODULE, "CONSTANTS.INACTIVE"), color: "bg-gray-500" },
  { value: "error", label: tStatic(UTILITIES_CONSOLE_MODULE, COMMON_ERROR), color: LOG_ERROR_COLOR },
] as const;

export const getTraceStatus = () => [
  { value: "ok", label: tStatic(UTILITIES_CONSOLE_MODULE, "common:SUCCESS"), color: "bg-green-500" },
  { value: "error", label: tStatic(UTILITIES_CONSOLE_MODULE, COMMON_ERROR), color: LOG_ERROR_COLOR },
  { value: "timeout", label: tStatic(UTILITIES_CONSOLE_MODULE, "CONSTANTS.TIMEOUT"), color: "bg-yellow-500" },
] as const;
