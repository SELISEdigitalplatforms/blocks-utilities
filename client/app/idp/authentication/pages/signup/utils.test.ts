import { describe, expect, it } from "vitest";
import { signupFormDefaultValue, createSignupFormSchema } from "./utils";

describe("signup form schema", () => {
  it("has an empty email default", () => {
    expect(signupFormDefaultValue.email).toBe("");
  });

  it("accepts a valid email", () => {
    expect(createSignupFormSchema().safeParse({ email: "a@b.com" }).success).toBe(true);
  });

  it("rejects an invalid email", () => {
    expect(createSignupFormSchema().safeParse({ email: "bad" }).success).toBe(false);
  });
});
