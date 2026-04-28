import { getRuntimeEnv } from "@/lib/runtime-env";

export const getApiPath = (_servicePath: string): string => {
  return "/api";
};

export const getApiUrl = (_servicePath: string, endpoint: string): string => {
  const baseUrl = getRuntimeEnv("BLOCKS_API_BASE_URL");
  return `${baseUrl}/api/${endpoint}`;
};
