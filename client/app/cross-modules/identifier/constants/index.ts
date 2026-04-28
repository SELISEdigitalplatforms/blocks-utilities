export enum REGISTER_SERVICE_TYPE {
  None = 0,
  Api = 1,
  Worker = 2,
}

export const REGISTER_SERVICE_TYPES: { value: REGISTER_SERVICE_TYPE; label: string }[] = [
  { value: REGISTER_SERVICE_TYPE.None, label: "None" },
  { value: REGISTER_SERVICE_TYPE.Api, label: "API" },
  { value: REGISTER_SERVICE_TYPE.Worker, label: "Worker" },
];

export const REGISTER_SERVICE_ENVIRONMENTS = [
  { value: "prod", label: "Production" },
  { value: "stg", label: "Staging" },
  { value: "dev", label: "Development" },
];

export const LOG_LEVELS = [
  { value: "debug", label: "Debug", color: "bg-gray-500" },
  { value: "info", label: "Info", color: "bg-blue-500" },
  { value: "warn", label: "Warning", color: "bg-yellow-500" },
  { value: "error", label: "Error", color: "bg-red-500" },
  { value: "fatal", label: "Fatal", color: "bg-red-700" },
] as const;

export const SERVICE_STATUS = [
  { value: "active", label: "Active", color: "bg-green-500" },
  { value: "inactive", label: "Inactive", color: "bg-gray-500" },
  { value: "error", label: "Error", color: "bg-red-500" },
] as const;

export const TRACE_STATUS = [
  { value: "ok", label: "Success", color: "bg-green-500" },
  { value: "error", label: "Error", color: "bg-red-500" },
  { value: "timeout", label: "Timeout", color: "bg-yellow-500" },
] as const;
