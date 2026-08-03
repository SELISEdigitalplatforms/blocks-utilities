import { describe, expect, it } from "vitest";
import { authConfigFormDefaultValues, authConfigFormSchema } from "./utils";

const validForm = {
  refreshTokenValidForNumberMinutes: 60,
  getNumberOfWrongAttemptsToLockTheAccount: 5,
  accountLockDurationInMinutes: 15,
  accessTokenValidForNumberMinutes: 30,
  rememberMeRefreshTokenValidForNumberMinutes: 1440,
};

describe("auth config form schema", () => {
  it("has zeroed defaults", () => {
    expect(authConfigFormDefaultValues.accessTokenValidForNumberMinutes).toBe(0);
  });

  it("accepts positive whole numbers", () => {
    expect(authConfigFormSchema.safeParse(validForm).success).toBe(true);
  });

  it("coerces numeric strings", () => {
    const result = authConfigFormSchema.safeParse({
      ...validForm,
      refreshTokenValidForNumberMinutes: "120",
    });
    expect(result.success).toBe(true);
  });

  it("rejects zero and negative values", () => {
    expect(
      authConfigFormSchema.safeParse({
        ...validForm,
        accountLockDurationInMinutes: 0,
      }).success,
    ).toBe(false);
  });

  it("rejects non-integer values", () => {
    expect(
      authConfigFormSchema.safeParse({
        ...validForm,
        accessTokenValidForNumberMinutes: 1.5,
      }).success,
    ).toBe(false);
  });

  it("rejects values above the max", () => {
    expect(
      authConfigFormSchema.safeParse({
        ...validForm,
        accessTokenValidForNumberMinutes: 2147483648,
      }).success,
    ).toBe(false);
  });
});
