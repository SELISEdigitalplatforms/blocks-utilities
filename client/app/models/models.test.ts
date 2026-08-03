import { describe, expect, it } from "vitest";
import {
  STORAGE_STRATEGIES,
  DmsItemType,
} from "@/cross-modules/storage/models/storage.model";
import { ResourceType } from "@/idp/iam/models/role";
import { modules, translation } from "@/cross-modules/localization/models/language";
import {
  CAPTCHA_PROVIDERS,
  CAPTCHA_GENERATOR_TYPE,
} from "@/idp/captcha/models/captcha";
import {
  organizationConfigFormSchema,
  organizationConfigFormDefaultValues,
} from "@/idp/iam/models/organization-config.model";
import { status } from "@/idp/iam/models/user";
import { providers } from "@/cross-modules/devops/models/git-dummy";

describe("storage model", () => {
  it("lists storage strategies with unique ids", () => {
    expect(STORAGE_STRATEGIES.map((s) => s.value)).toEqual([
      "Amazon",
      "Azure",
      "SftpStorage",
      "S3Compatible",
    ]);
  });

  it("defines DmsItemType numeric values", () => {
    expect(DmsItemType.File).toBe(1);
    expect(DmsItemType.Folder).toBe(2);
  });
});

describe("role model", () => {
  it("defines ResourceType values", () => {
    expect(ResourceType.Endpoint).toBe(1);
    expect(ResourceType["Data protection"]).toBe(3);
  });
});

describe("language model", () => {
  it("exposes module and translation option lists", () => {
    expect(modules.map((m) => m.value)).toContain("UILM Tool");
    expect(translation.map((t) => t.value)).toContain("Complete");
  });
});

describe("captcha model", () => {
  it("defines providers and generator types", () => {
    expect(CAPTCHA_PROVIDERS.recaptcha.label).toBe("Google reCAPTCHA");
    expect(CAPTCHA_GENERATOR_TYPE.HardCaptchaGenerator.value).toBe(
      "HardCaptchaGenerator",
    );
  });
});

describe("organization config model", () => {
  it("provides valid default values that satisfy the schema", () => {
    const result = organizationConfigFormSchema.safeParse(
      organizationConfigFormDefaultValues,
    );
    expect(result.success).toBe(true);
    expect(organizationConfigFormDefaultValues.allowCreationFromCloud).toBe(true);
  });

  it("rejects a non-boolean field", () => {
    const result = organizationConfigFormSchema.safeParse({
      isMultiOrgEnabled: "yes",
      allowCreationFromCloud: true,
      allowCreationFromConstruct: false,
    });
    expect(result.success).toBe(false);
  });
});

describe("user model", () => {
  it("lists user status options", () => {
    expect(status.map((s) => s.value)).toEqual([
      "Active",
      "Inactive",
      "Verified",
    ]);
  });
});

describe("devops git-dummy model", () => {
  it("marks only github as active", () => {
    expect(providers.find((p) => p.id === "github")?.active).toBe(true);
    expect(providers.find((p) => p.id === "gitlab")?.active).toBe(false);
  });
});
