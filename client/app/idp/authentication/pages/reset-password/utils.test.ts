import { describe, expect, it } from "vitest";
import { activationFormDefaultValue, activationFormSchema } from "./utils";

describe("reset-password form schema", () => {
  it("has empty defaults", () => {
    expect(activationFormDefaultValue.password).toBe("");
  });

  it("accepts a strong matching password", () => {
    const result = activationFormSchema.safeParse({
      password: "Str0ng!pass",
      confirmPassword: "Str0ng!pass",
    });
    expect(result.success).toBe(true);
  });

  it("rejects a weak password", () => {
    const result = activationFormSchema.safeParse({
      password: "weakpass",
      confirmPassword: "weakpass",
    });
    expect(result.success).toBe(false);
  });

  it("rejects mismatched confirmation", () => {
    const result = activationFormSchema.safeParse({
      password: "Str0ng!pass",
      confirmPassword: "Different1!",
    });
    expect(result.success).toBe(false);
  });
});
