import { getLanguageCode, tStatic } from "@seliseblocks/genesis-os";

export const SUBSCRIPTION_I18N_MODULE = "utilities.subscription";

/**
 * Text for the subscription module from outside a component (formatters, schemas, option lists).
 * Call it when the value is used, never at import time, so a language change is picked up.
 */
export const tSubscription = (key: string, values?: Record<string, string | number>): string =>
  tStatic(SUBSCRIPTION_I18N_MODULE, key, values);

/**
 * "a, b and c" in the UI language. English is read with the British list rules so the last
 * pair has no serial comma, as the copy was written.
 */
export const joinList = (parts: string[], type: "conjunction" | "disjunction" = "conjunction"): string => {
  const language = getLanguageCode();

  return new Intl.ListFormat(language === "en" ? "en-GB" : language, { type }).format(parts);
};
