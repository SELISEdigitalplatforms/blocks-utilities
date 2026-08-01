import { describe, expect, it } from "vitest";
import {
  forgotPasswordFormDefaultValue,
  forgotPasswordFormSchema,
} from "./utils";

describe("oidc forgot-password form schema", () => {
  it("has an empty email default", () => {
    expect(forgotPasswordFormDefaultValue.email).toBe("");
  });

  it("accepts a valid email", () => {
    expect(forgotPasswordFormSchema.safeParse({ email: "a@b.com" }).success).toBe(
      true,
    );
  });

  it("rejects an invalid email", () => {
    expect(forgotPasswordFormSchema.safeParse({ email: "x" }).success).toBe(false);
  });
});
