import { renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { usePasswordStrength } from "./use-password-strength";

describe("usePasswordStrength", () => {
  it("should return zero strength for empty password", () => {
    const { result } = renderHook(() => usePasswordStrength(""));

    expect(result.current.strength).toBe(0);
    expect(result.current.allRequirementsMet).toBe(false);
  });

  it("should return full strength for a strong password", () => {
    const { result } = renderHook(() => usePasswordStrength("Test@1234"));

    expect(result.current.strength).toBe(100);
    expect(result.current.allRequirementsMet).toBe(true);
  });

  it("should return partial strength for a weak password", () => {
    const { result } = renderHook(() => usePasswordStrength("testTest1"));

    expect(result.current.strength).toBeGreaterThan(0);
    expect(result.current.strength).toBeLessThan(100);
    expect(result.current.allRequirementsMet).toBe(false);
  });

  it("should update checks when password changes", () => {
    const { result, rerender } = renderHook(({ password }) => usePasswordStrength(password), {
      initialProps: { password: "" },
    });

    expect(result.current.allRequirementsMet).toBe(false);

    rerender({ password: "Test@1234" });

    expect(result.current.allRequirementsMet).toBe(true);
    expect(result.current.strength).toBe(100);
  });

  it("should return checks object with boolean values", () => {
    const { result } = renderHook(() => usePasswordStrength("Test@1234"));

    expect(result.current.checks).toBeDefined();
    expect(typeof result.current.checks.length).toBe("boolean");
    expect(typeof result.current.checks.case).toBe("boolean");
    expect(typeof result.current.checks.number).toBe("boolean");
    expect(typeof result.current.checks.special).toBe("boolean");
  });

  it("should return a strength color", () => {
    const { result } = renderHook(() => usePasswordStrength("Test@1234"));

    const color = result.current.getStrengthColor();
    expect(typeof color).toBe("string");
    expect(color.length).toBeGreaterThan(0);
  });

  it("should return requirements array", () => {
    const { result } = renderHook(() => usePasswordStrength(""));

    expect(result.current.requirements).toBeDefined();
    expect(Array.isArray(result.current.requirements)).toBe(true);
    expect(result.current.requirements.length).toBeGreaterThan(0);
  });
});
