import { describe, expect, it } from "vitest";
import {
  authGrantTypeFormDefaultValues,
  authGrantTypeFormSchema,
} from "./utils";

describe("auth grant-type form schema", () => {
  it("defaults to an empty list", () => {
    expect(authGrantTypeFormDefaultValues.allowedGrantTypes).toEqual([]);
  });

  it("accepts at least one grant type", () => {
    expect(
      authGrantTypeFormSchema.safeParse({ allowedGrantTypes: ["password"] })
        .success,
    ).toBe(true);
  });

  it("rejects an empty list", () => {
    expect(
      authGrantTypeFormSchema.safeParse({ allowedGrantTypes: [] }).success,
    ).toBe(false);
  });
});
