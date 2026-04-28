import { isValidDomain } from "@/lib/domain";
import * as z from "zod";

export const editProjectFormDefaultValue = {
  name: "",
  applicationDomain: "https://",
  isCookieEnable: true,
  cookieDomain: "",
  useCustomDomain: false,
  customDomain: "",
};

export const editProjectFormSchema = z
  .object({
    name: z
      .string()
      .min(1, "Name is required")
      .min(3, "Project name must be at least 3 characters")
      .max(100, "Project name should be a maximum of 100 characters"),
    applicationDomain: z
      .string()
      .trim()
      .min(1, "Application domain is required")
      .refine((s) => isValidDomain(s + "seliseblocks.com"), "Invalid domain format"),
    isCookieEnable: z.boolean(),
    cookieDomain: z.string().optional(),
    useCustomDomain: z.boolean(),
    customDomain: z
      .string()
      .optional()
      .refine((s) => !s || s.trim() === "" || isValidDomain(s), "Invalid custom domain format"),
  })
  .refine(
    (data) => {
      if (data.useCustomDomain && (!data.customDomain || data.customDomain.trim() === "")) {
        return false;
      }
      return true;
    },
    {
      message: "Enter a valid custom domain",
      path: ["customDomain"],
    },
  );
