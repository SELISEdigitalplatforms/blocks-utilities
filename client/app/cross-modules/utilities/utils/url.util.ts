import { SHORT_URL_BASES } from "@blocks-utilities/constants/endpoint.constant";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { z } from "zod";

export const getDefaultShortUrlBase = (): string => {
  const apiBase = getRuntimeEnv("BLOCKS_API_BASE_URL") || "";

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

export const magicUrlSchema = z.object({
  uri: z
    .string()
    .min(1, "URI is required")
    .regex(/^(https?:\/\/)?([\da-z.-]+)\.([a-z.]{2,6})([/\w .-]*)*\/?$/, "Please enter a valid URI")
    .refine(
      (val) => {
        try {
          new URL(val.startsWith("http") ? val : `https://${val}`);
          return true;
        } catch {
          return false;
        }
      },
      { message: "Please enter a valid URI" },
    ),
  name: z.string().min(1, "Name is required").max(100, "Name must be at most 100 characters"),
});
