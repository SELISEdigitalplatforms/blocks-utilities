import { getRuntimeEnv } from "@/lib/runtime-env";

/**
 * Returns the global API path prefix. All app API routes use `/api` (same in every environment).
 * @param _servicePath - Legacy; ignored. Kept for call-site compatibility.
 */
export const getApiPath = (_servicePath: string): string => {
  return "/api";
};

/**
 * Constructs a full API URL: base origin + `/api` + `/${endpoint}`.
 * @param _servicePath - Legacy; ignored. Kept for call-site compatibility.
 * @param endpoint - The path after `/api` (e.g. `Authentication/Login`, `.well-known/jwks.json`)
 */
export const getApiUrl = (_servicePath: string, endpoint: string): string => {
  const baseUrl = getRuntimeEnv("BLOCKS_API_BASE_URL");
  const apiPath = getApiPath(_servicePath);
  return `${baseUrl}${apiPath}/${endpoint}`;
};
