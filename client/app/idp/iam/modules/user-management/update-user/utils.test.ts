import { describe, expect, it } from "vitest";
import { inviteUserFormDefaultValue, createInviteUserFormSchema } from "./utils";

describe("invite user form schema", () => {
  it("has empty defaults", () => {
    expect(inviteUserFormDefaultValue).toEqual({ firstName: "", lastName: "" });
  });

  it("accepts valid names", () => {
    expect(
      createInviteUserFormSchema().safeParse({ firstName: "Jane", lastName: "Doe" })
        .success,
    ).toBe(true);
  });

  it("rejects an empty first name", () => {
    expect(
      createInviteUserFormSchema().safeParse({ firstName: "", lastName: "Doe" }).success,
    ).toBe(false);
  });

  it("rejects an over-long last name", () => {
    expect(
      createInviteUserFormSchema().safeParse({
        firstName: "Jane",
        lastName: "a".repeat(151),
      }).success,
    ).toBe(false);
  });
});
