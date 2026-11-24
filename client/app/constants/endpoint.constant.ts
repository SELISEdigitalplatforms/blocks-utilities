import { getMergedAreaBaseUrl } from "@seliseblocks/genesis-os/lib";
import { getRuntimeEnv } from "../lib/runtime-env";

export const API_BASES = {
  COMMUNICATION: "/api",
  CLOUD_CONFIGURATION: "/api",
  UDS: "/api",
  UILM: "/api",
  UTILITIES: "/api",
  CLOUD_BUILD: "/api",
  IDP: getRuntimeEnv("BLOCKS_IAM_BASE_URL") + "/api",
  IDENTIFIER: "/api",
  LMT: "/api",
  MFA: "/api",
  ALERT: "/api",
  AI: "/api",
  STUDIO: "/api",
  // Logic is served by the os host: BLOCKS_LOGIC_BASE_URL while set, else BLOCKS_OS_BASE_URL.
  LOGIC: getMergedAreaBaseUrl("BLOCKS_LOGIC_BASE_URL") + "/api",
} as const;
