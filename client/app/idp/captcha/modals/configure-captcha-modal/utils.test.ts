import { describe, expect, it } from "vitest";
import {
  ConfigureCaptchaFormSchema,
  ConfigureCaptchaFormDefaultValue,
} from "./utils";

describe("configure captcha form schema", () => {
  it("has empty defaults", () => {
    expect(ConfigureCaptchaFormDefaultValue.provider).toBe("");
  });

  it("accepts a valid configuration", () => {
    const result = ConfigureCaptchaFormSchema.safeParse({
      provider: "recaptcha",
      captchaKey: "site",
      captchaSecret: "secret",
      captchaGenerator: "EasyCaptchaGenerator",
    });
    expect(result.success).toBe(true);
  });

  it("rejects an unknown provider", () => {
    const result = ConfigureCaptchaFormSchema.safeParse({
      provider: "unknown",
      captchaKey: "site",
      captchaSecret: "secret",
      captchaGenerator: "EasyCaptchaGenerator",
    });
    expect(result.success).toBe(false);
  });

  it("requires the site and secret keys", () => {
    const result = ConfigureCaptchaFormSchema.safeParse({
      provider: "hcaptcha",
      captchaKey: "",
      captchaSecret: "",
      captchaGenerator: "",
    });
    expect(result.success).toBe(false);
  });
});
