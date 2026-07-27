import { describe, expect, it } from "vitest";
import {
  selfSignUpFormDefaultValues,
  selfSignUpFormSchema,
} from "./utils";

describe("self sign-up form schema", () => {
  it("defaults to disabled", () => {
    expect(selfSignUpFormDefaultValues.isSelfSignUpAllowed).toBe(false);
  });

  it("accepts a boolean value", () => {
    expect(
      selfSignUpFormSchema.safeParse({ isSelfSignUpAllowed: true }).success,
    ).toBe(true);
  });

  it("rejects a non-boolean value", () => {
    expect(
      selfSignUpFormSchema.safeParse({ isSelfSignUpAllowed: "yes" }).success,
    ).toBe(false);
  });
});
