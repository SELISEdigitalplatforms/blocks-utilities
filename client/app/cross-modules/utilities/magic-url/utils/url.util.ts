import { SHORT_URL_BASES } from "@blocks-utilities/magic-url/constants/endpoint.constant";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { tStatic } from "@seliseblocks/genesis-os";
import { z } from "zod";

const UTILITIES_MAGIC_URL_MODULE = "utilities.magic-url";

export const getDefaultShortUrlBase = (): string => {
  const apiBase = getRuntimeEnv("BLOCKS_UTILITIES_BASE_URL") || "";

  const match = Object.entries(SHORT_URL_BASES).find(
    ([env]) => env !== "prod" && apiBase.includes(env),
  );

  return match ? match[1] : SHORT_URL_BASES.prod;
};

export const isValidUrl = (url: string): boolean => {
  try {
    const urlObj = new URL(url);
    if (urlObj.protocol !== "http:" && urlObj.protocol !== "https:") {
      return false;
    }
    return true;
  } catch {
    return false;
  }
};

export const createMagicUrlSchema = () =>
  z.object({
  uri: z
    .string()
    .min(1, tStatic(UTILITIES_MAGIC_URL_MODULE, "URL_UTIL.URI_REQUIRED"))
    .regex(
      /^(https?:\/\/)?([\da-z.-]+)\.([a-z]{2,6})([/\w .-]*)\/?$/,
      tStatic(UTILITIES_MAGIC_URL_MODULE, "URL_UTIL.INVALID_URI"),
    )
    .refine(
      (val) => {
        try {
          new URL(val.startsWith("http") ? val : `https://${val}`);
          return true;
        } catch {
          return false;
        }
      },
      { message: tStatic(UTILITIES_MAGIC_URL_MODULE, "URL_UTIL.INVALID_URI") },
    ),
  name: z
    .string()
    .min(1, tStatic(UTILITIES_MAGIC_URL_MODULE, "URL_UTIL.NAME_REQUIRED"))
    .max(100, tStatic(UTILITIES_MAGIC_URL_MODULE, "URL_UTIL.NAME_TOO_LONG")),
});
