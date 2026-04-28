import { z } from "zod";

export const createOidcSchema = z.object({
redirectUrlOidc: z.string().url("Must be a valid URL").refine((val) => {
  try {
    const url = new URL(val);
    if (url.hostname === "localhost" || url.hostname === "127.0.0.1") {
      return url.protocol === "http:" || url.protocol === "https:";
    }
    return url.protocol === "https:";
  } catch {
    return false;
  }
}, "Only HTTP is allowed for localhost. All other URLs must use HTTPS."),  
audienceUrlOidc: z.string().url("Must be a valid URL").refine((val) => {
  try {
    const url = new URL(val);
    if (url.hostname === "localhost" || url.hostname === "127.0.0.1") {
      return url.protocol === "http:" || url.protocol === "https:";
    }
    return url.protocol === "https:";
  } catch {
    return false;
  }
}, "Only HTTP is allowed for localhost. All other URLs must use HTTPS."), 
  scope: z.string().trim(),
  clientBrandColor: z.string().optional(),
  clientDisplayName: z.string().trim().min(1, "Client display name is required"),
});

export type CreateOIDCFormValues = z.infer<typeof createOidcSchema>;

export const createOIDCFormDefaultValue: CreateOIDCFormValues = {
  audienceUrlOidc: "",
  redirectUrlOidc: "",
  scope: "openid",
  clientBrandColor: "#FFFFFF",
  clientDisplayName: "",
};
