import { isValidDomain } from "@/lib/domain";
import * as z from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_SHARED_MODULE = "utilities.shared";

export const editProjectFormDefaultValue = {
  name: "",
  applicationDomain: "https://",
  isCookieEnable: true,
  cookieDomain: "",
  useCustomDomain: false,
  customDomain: "",
};

export const editProjectFormSchema = () =>
  z
  .object({
    name: z
      .string()
      .min(1, tStatic(UTILITIES_SHARED_MODULE, "EDIT_PROJECT_FORM.NAME_REQUIRED"))
      .min(3, tStatic(UTILITIES_SHARED_MODULE, "EDIT_PROJECT_FORM.NAME_MIN_LENGTH"))
      .max(100, tStatic(UTILITIES_SHARED_MODULE, "EDIT_PROJECT_FORM.NAME_MAX_LENGTH")),
    applicationDomain: z
      .string()
      .trim()
      .min(1, tStatic(UTILITIES_SHARED_MODULE, "EDIT_PROJECT_FORM.APPLICATION_DOMAIN_REQUIRED"))
      .refine((s) => isValidDomain(s + "seliseblocks.com"), tStatic(UTILITIES_SHARED_MODULE, "EDIT_PROJECT_FORM.INVALID_DOMAIN_FORMAT")),
    isCookieEnable: z.boolean(),
    cookieDomain: z.string().optional(),
    useCustomDomain: z.boolean(),
    customDomain: z
      .string()
      .optional()
      .refine((s) => !s || s.trim() === "" || isValidDomain(s), tStatic(UTILITIES_SHARED_MODULE, "EDIT_PROJECT_FORM.INVALID_CUSTOM_DOMAIN_FORMAT")),
  })
  .refine(
    (data) => {
      if (data.useCustomDomain && (!data.customDomain || data.customDomain.trim() === "")) {
        return false;
      }
      return true;
    },
    {
      message: tStatic(UTILITIES_SHARED_MODULE, "EDIT_PROJECT_FORM.ENTER_VALID_CUSTOM_DOMAIN"),
      path: ["customDomain"],
    },
  );
