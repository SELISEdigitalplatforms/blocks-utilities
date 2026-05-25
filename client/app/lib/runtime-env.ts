const PLACEHOLDER_PREFIX = "__BLOCKS_";

type RuntimeKey =
  | "BLOCKS_IAM_BASE_URL"
  | "BLOCKS_X_BLOCKS_KEY"
  | "BLOCKS_GOOGLE_SITE_KEY"
  | "BLOCKS_CONSTRUCT_URL"
  | "BLOCKS_GITHUB_SSO_CLIENT_ID"
  | "BLOCKS_OIDC_CLIENT_ID"
  | "BLOCKS_BASE_DOMAIN"
  | "BLOCKS_IAM_CALLBACK_URL"
  | "BLOCKS_LOCALIZATION_BASE_URL"
  | "BLOCKS_LOCALIZATION_CALLBACK_URL"
  | "BLOCKS_AGENTS_BASE_URL"
  | "BLOCKS_AGENTS_CALLBACK_URL"
  | "BLOCKS_DATA_BASE_URL"
  | "BLOCKS_DATA_CALLBACK_URL"
  | "BLOCKS_OS_BASE_URL"
  | "BLOCKS_OS_CALLBACK_URL"
  | "BLOCKS_UTILITIES_BASE_URL"
  | "BLOCKS_UTILITIES_CALLBACK_URL"
  | "BLOCKS_LOGIC_BASE_URL"
  | "BLOCKS_LOGIC_CALLBACK_URL"
  | "BLOCKS_MONITOR_BASE_URL"
  | "BLOCKS_MONITOR_CALLBACK_URL"
  | "BLOCKS_RELEASE_BASE_URL"
  | "BLOCKS_RELEASE_CALLBACK_URL"
  | "BLOCKS_STUDIO_BASE_URL"
  | "BLOCKS_STUDIO_CALLBACK_URL";

declare global {
  interface Window {
    __BLOCKS_ENV__?: Partial<Record<RuntimeKey, string>>;
  }
}

const isPlaceholder = (value?: string) =>
  !!value && value.startsWith(PLACEHOLDER_PREFIX) && value.endsWith("__");

export const getRuntimeEnv = (key: RuntimeKey): string => {
  const windowValue =
    typeof window !== "undefined" ? window.__BLOCKS_ENV__?.[key] : undefined;
  if (windowValue && !isPlaceholder(windowValue)) {
    return windowValue;
  }

  return import.meta.env[key] || "";
};
