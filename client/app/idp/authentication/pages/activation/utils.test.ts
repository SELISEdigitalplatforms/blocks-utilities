import { describe, expect, it } from "vitest";
import { activationFormDefaultValue, createActivationFormSchema } from "./utils";

describe("activation form schema", () => {
  it("has empty defaults", () => {
    expect(activationFormDefaultValue.firstname).toBe("");
  });

  it("accepts a valid activation without whitespace", () => {
    const result = createActivationFormSchema().safeParse({
      firstname: "Jane",
      lastname: "Doe",
      password: "secret",
      confirmPassword: "secret",
    });
    expect(result.success).toBe(true);
    if (result.success) expect(result.data.password).toBe("secret");
  });

  it("rejects a password containing spaces", () => {
    const result = createActivationFormSchema().safeParse({
      firstname: "Jane",
      lastname: "Doe",
      password: "sec ret",
      confirmPassword: "sec ret",
    });
    expect(result.success).toBe(false);
  });

  it("requires first and last name", () => {
    const result = createActivationFormSchema().safeParse({
      firstname: "",
      lastname: "",
      password: "secret",
      confirmPassword: "secret",
    });
    expect(result.success).toBe(false);
  });
});
