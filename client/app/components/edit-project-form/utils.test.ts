import { describe, expect, it } from "vitest";
import { editProjectFormDefaultValue, editProjectFormSchema } from "./utils";

const base = {
  name: "My Project",
  applicationDomain: "https://app.",
  isCookieEnable: true,
  cookieDomain: "",
  useCustomDomain: false,
  customDomain: "",
};

describe("editProjectFormSchema", () => {
  it("provides a starting default value", () => {
    expect(editProjectFormDefaultValue.applicationDomain).toBe("https://");
  });

  it("accepts a valid form without a custom domain", () => {
    expect(editProjectFormSchema.safeParse(base).success).toBe(true);
  });

  it("rejects a short name", () => {
    expect(
      editProjectFormSchema.safeParse({ ...base, name: "ab" }).success,
    ).toBe(false);
  });

  it("requires a custom domain when useCustomDomain is on", () => {
    const result = editProjectFormSchema.safeParse({
      ...base,
      useCustomDomain: true,
      customDomain: "",
    });
    expect(result.success).toBe(false);
  });

  it("accepts a valid custom domain when enabled", () => {
    const result = editProjectFormSchema.safeParse({
      ...base,
      useCustomDomain: true,
      customDomain: "https://custom.example.com",
    });
    expect(result.success).toBe(true);
  });

  it("rejects an invalid custom domain", () => {
    const result = editProjectFormSchema.safeParse({
      ...base,
      useCustomDomain: true,
      customDomain: "not a domain",
    });
    expect(result.success).toBe(false);
  });
});
