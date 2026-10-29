import { z } from "zod";

export const ConfigureCaptchaFormSchema = z.object({
  // zod 3's default messages, kept so the form reads the same after the zod 4 upgrade. The
  // form starts with "" here, so submitting without choosing a provider shows the second one.
  provider: z.enum(["recaptcha", "hcaptcha"], {
    error: (issue) =>
      issue.input === undefined
        ? "Required"
        : `Invalid enum value. Expected 'recaptcha' | 'hcaptcha', received '${String(issue.input)}'`,
  }),
  captchaKey: z.string().min(1, "Site key is required"),
  captchaSecret: z.string().min(1, "Secret key is required"),
  captchaGenerator: z.string().min(1, "Generator type is required"),
});
export const ConfigureCaptchaFormDefaultValue = {
  provider: "",
  captchaKey: "",
  captchaSecret: "",
  captchaGenerator: "",
};
