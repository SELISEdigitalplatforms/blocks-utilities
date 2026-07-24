import { describe, expect, it } from "vitest";
import { getErrorMessage, isErrorWithErrors, handleErrorMessages } from "./error";

describe("getErrorMessage", () => {
  it("returns a fallback for empty input", () => {
    expect(getErrorMessage({})).toBe("Something went wrong.");
  });

  it("prefers a mapped message when the key is present", () => {
    expect(getErrorMessage({ email: "raw" }, { email: "Nice message" })).toEqual([
      "Nice message",
    ]);
  });

  it("collects string and array values", () => {
    expect(
      getErrorMessage({ a: "one", b: ["two", "three"] }),
    ).toEqual(["one", "two, three"]);
  });

  it("returns fallback when no usable messages exist", () => {
    expect(getErrorMessage({ a: [] })).toBe("Something went wrong.");
  });
});

describe("isErrorWithErrors", () => {
  it("detects an object carrying an errors field", () => {
    expect(isErrorWithErrors({ errors: { a: "x" } })).toBe(true);
  });

  it("rejects other shapes", () => {
    expect(isErrorWithErrors(null)).toBe(false);
    expect(isErrorWithErrors("nope")).toBe(false);
    expect(isErrorWithErrors({ other: 1 })).toBe(false);
  });
});

describe("handleErrorMessages", () => {
  it("passes strings through unchanged", () => {
    expect(handleErrorMessages("boom")).toBe("boom");
  });

  it("delegates object errors to getErrorMessage", () => {
    expect(handleErrorMessages({ a: "one" })).toEqual(["one"]);
  });

  it("returns a generic message for arrays or unknowns", () => {
    expect(handleErrorMessages([1, 2])).toBe("An unexpected error occurred.");
    expect(handleErrorMessages(42)).toBe("An unexpected error occurred.");
  });
});
