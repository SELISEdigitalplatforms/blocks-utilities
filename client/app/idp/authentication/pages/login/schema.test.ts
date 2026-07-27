import { describe, expect, it } from "vitest";
import { signinFormDefaultValue, signinFormSchema } from "./schema";

describe("signin form schema", () => {
  it("has empty defaults", () => {
    expect(signinFormDefaultValue).toEqual({ username: "", password: "" });
  });

  it("accepts a valid email and password", () => {
    expect(
      signinFormSchema.safeParse({ username: "a@b.com", password: "x" }).success,
    ).toBe(true);
  });

  it("rejects an invalid email", () => {
    expect(
      signinFormSchema.safeParse({ username: "nope", password: "x" }).success,
    ).toBe(false);
  });

  it("requires a password", () => {
    expect(
      signinFormSchema.safeParse({ username: "a@b.com", password: "" }).success,
    ).toBe(false);
  });
});
