import { z } from "zod";
import { tStatic } from "@seliseblocks/genesis-os";

export const createConfigureCaptchaFormSchema = () => z.object({
  provider: z.enum(["recaptcha", "hcaptcha"]),
  captchaKey: z.string().min(1, tStatic("utilities.idp", "UTILS.SITE_KEY_IS_REQUIRED")),
  captchaSecret: z.string().min(1, tStatic("utilities.idp", "UTILS.SECRET_KEY_IS_REQUIRED")),
  captchaGenerator: z.string().min(1, tStatic("utilities.idp", "UTILS.GENERATOR_TYPE_IS_REQUIRED")),
});
export const ConfigureCaptchaFormDefaultValue = {
  provider: "",
  captchaKey: "",
  captchaSecret: "",
  captchaGenerator: "",
};
