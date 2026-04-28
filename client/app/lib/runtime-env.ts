const PLACEHOLDER_PREFIX = "__BLOCKS_";

type RuntimeKey =
  | "BLOCKS_API_BASE_URL"
  | "BLOCKS_X_BLOCKS_KEY"
  | "BLOCKS_GOOGLE_SITE_KEY"
  | "BLOCKS_CONSTRUCT_URL";

declare global {
  interface Window {
    __BLOCKS_ENV__?: Partial<Record<RuntimeKey, string>>;
  }
}

const isPlaceholder = (value?: string) =>
  !!value && value.startsWith(PLACEHOLDER_PREFIX) && value.endsWith("__");

export const getRuntimeEnv = (key: RuntimeKey): string => {
  const windowValue = typeof window !== "undefined" ? window.__BLOCKS_ENV__?.[key] : undefined;
  if (windowValue && !isPlaceholder(windowValue)) {
    return windowValue;
  }

  return import.meta.env[key] || "";
};
