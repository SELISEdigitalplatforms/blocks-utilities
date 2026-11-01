import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_IDP_AUTHENTICATION_MODULE = "utilities.idp.authentication";

export const makeOidcSchema = () => z.object({
redirectUrlOidc: z.string().url(tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "UTILS.MUST_BE_A_VALID_URL")).refine((val) => {
  try {
    const url = new URL(val);
    if (url.hostname === "localhost" || url.hostname === "127.0.0.1") {
      return url.protocol === "http:" || url.protocol === "https:";
    }
    return url.protocol === "https:";
  } catch {
    return false;
  }
}, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "UTILS.ONLY_HTTP_IS_ALLOWED_FOR")),  
audienceUrlOidc: z.string().url(tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "UTILS.MUST_BE_A_VALID_URL")).refine((val) => {
  try {
    const url = new URL(val);
    if (url.hostname === "localhost" || url.hostname === "127.0.0.1") {
      return url.protocol === "http:" || url.protocol === "https:";
    }
    return url.protocol === "https:";
  } catch {
    return false;
  }
}, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "UTILS.ONLY_HTTP_IS_ALLOWED_FOR")), 
  scope: z.string().trim(),
  clientBrandColor: z.string().optional(),
  clientDisplayName: z.string().trim().min(1, tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "UTILS.CLIENT_DISPLAY_NAME_IS_REQUIRED")),
});

export type CreateOIDCFormValues = z.infer<ReturnType<typeof makeOidcSchema>>;

export const createOIDCFormDefaultValue: CreateOIDCFormValues = {
  audienceUrlOidc: "",
  redirectUrlOidc: "",
  scope: "openid",
  clientBrandColor: "#FFFFFF",
  clientDisplayName: "",
};
