import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_IDP_MODULE = "utilities.idp";

export const createConfigureCaptchaFormSchema = () => z.object({
  // zod 3's default messages, kept so the form reads the same after the zod 4 upgrade. The
  // form starts with "" here, so submitting without choosing a provider shows the second one.
  provider: z.enum(["recaptcha", "hcaptcha"], {
    error: (issue) =>
      issue.input === undefined
        ? "Required"
        : `Invalid enum value. Expected 'recaptcha' | 'hcaptcha', received '${String(issue.input)}'`,
  }),
  captchaKey: z.string().min(1, tStatic(UTILITIES_IDP_MODULE, "UTILS.SITE_KEY_IS_REQUIRED")),
  captchaSecret: z.string().min(1, tStatic(UTILITIES_IDP_MODULE, "UTILS.SECRET_KEY_IS_REQUIRED")),
  captchaGenerator: z.string().min(1, tStatic(UTILITIES_IDP_MODULE, "UTILS.GENERATOR_TYPE_IS_REQUIRED")),
});
export const ConfigureCaptchaFormDefaultValue = {
  provider: "",
  captchaKey: "",
  captchaSecret: "",
  captchaGenerator: "",
};
