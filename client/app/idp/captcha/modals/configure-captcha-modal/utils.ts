import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_IDP_MODULE = "utilities.idp";

export const createConfigureCaptchaFormSchema = () => z.object({
  provider: z.enum(["recaptcha", "hcaptcha"]),
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
