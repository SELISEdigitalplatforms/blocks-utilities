import type { FieldValues, Path, UseFormSetError } from "react-hook-form";
import { PLACEHOLDER_PATTERN } from "../constants/sms.constants";

/** "RateLimit.TenantMaxPerWindow" → "rateLimit.tenantMaxPerWindow" (FluentValidation → form path). */
export const toFormPath = (serverField: string): string =>
  serverField
    .split(".")
    .map((segment) => segment.charAt(0).toLowerCase() + segment.slice(1))
    .join(".");

/**
 * Puts each server error under its field when the form has one; returns what is left over, for a
 * banner. Fields the form does not know (Tenant, Queue, Security...) are never silently dropped.
 */
export const applyServerErrors = <T extends FieldValues>(
  fieldErrors: Record<string, string>,
  knownFields: readonly string[],
  setError: UseFormSetError<T>,
): string[] => {
  const unplaced: string[] = [];

  Object.entries(fieldErrors).forEach(([field, message]) => {
    const path = toFormPath(field);
    if (knownFields.includes(path)) {
      setError(path as Path<T>, { type: "server", message });
    } else {
      unplaced.push(message);
    }
  });

  return unplaced;
};

/** Distinct placeholder keys, in order of first appearance, compared case-insensitively. */
export const extractPlaceholders = (body: string): string[] => {
  const seen = new Map<string, string>();
  for (const match of body.matchAll(PLACEHOLDER_PATTERN)) {
    const key = match[1];
    if (!seen.has(key.toLowerCase())) {
      seen.set(key.toLowerCase(), key);
    }
  }

  return [...seen.values()];
};

/**
 * SMS segments for a body: GSM-7 fits 160 characters (153 per part when split), anything outside
 * it forces UCS-2 at 70 (67). An estimate for the editor; carriers bill on what they count.
 */
export const countSegments = (body: string): { segments: number; encoding: "GSM-7" | "UCS-2" } => {
  // The GSM 03.38 basic set plus the extension table, which costs two characters each.
  const gsmBasic =
    "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";
  const gsmExtended = "^{}\\[~]|€";

  let gsmLength = 0;
  for (const character of body) {
    if (gsmBasic.includes(character)) {
      gsmLength += 1;
    } else if (gsmExtended.includes(character)) {
      gsmLength += 2;
    } else {
      const units = [...body].length;
      return { encoding: "UCS-2", segments: units === 0 ? 0 : units <= 70 ? 1 : Math.ceil(units / 67) };
    }
  }

  return {
    encoding: "GSM-7",
    segments: gsmLength === 0 ? 0 : gsmLength <= 160 ? 1 : Math.ceil(gsmLength / 153),
  };
};
