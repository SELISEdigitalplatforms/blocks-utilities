import { getRuntimeEnv } from "../lib/runtime-env";

export const API_BASES = {
  COMMUNICATION: "/api",
  CLOUD_CONFIGURATION: "/api",
  UDS: "/api",
  UILM: "/api",
  UTILITIES: "/api",
  CLOUD_BUILD: "/api",
  IDP: getRuntimeEnv("BLOCKS_IDP_BASE_URL") + "/api",
  IDENTIFIER: "/api",
  LMT: "/api",
  MFA: "/api",
  ALERT: "/api",
  AI: "/api",
  STUDIO: "/api",
  LOGIC: getRuntimeEnv("BLOCKS_LOGIC_BASE_URL") + "/api",
} as const;