import { describe, expect, it } from "vitest";
import {
  REGEX_PATTERNS,
  STRENGTH_MULTIPLIER,
  STRENGTH_THRESHOLDS,
  STRENGTH_COLORS,
  getPasswordRequirements,
  createInitialChecks,
  validatePasswordChecks,
  calculateStrength,
  areAllRequirementsMet,
  getStrengthColor,
} from "./password-strength.util";

describe("password-strength.util", () => {
  // ─── getPasswordRequirements ────────────────────────────────────────────────
  describe("getPasswordRequirements", () => {
    it("should return 4 requirements", () => {
      const requirements = getPasswordRequirements();
      expect(requirements).toHaveLength(4);
    });

    it("should include length, case, number, and special keys", () => {
      const requirements = getPasswordRequirements();
      const keys = requirements.map((r) => r.key);
      expect(keys).toEqual(["length", "case", "number", "special"]);
    });
  });

  // ─── createInitialChecks ────────────────────────────────────────────────────
  describe("createInitialChecks", () => {
    it("should return all checks as false", () => {
      const checks = createInitialChecks();
      expect(checks).toEqual({
        length: false,
        case: false,
        number: false,
        special: false,
      });
    });
  });

  // ─── validatePasswordChecks ─────────────────────────────────────────────────
  describe("validatePasswordChecks", () => {
    it("should return all false for empty password", () => {
      expect(validatePasswordChecks("")).toEqual({
        length: false,
        case: false,
        number: false,
        special: false,
      });
    });

    it("should validate length between 8 and 30", () => {
      expect(validatePasswordChecks("abcdefgh").length).toBe(true);
      expect(validatePasswordChecks("abcdefg").length).toBe(false);
      expect(validatePasswordChecks("a".repeat(31)).length).toBe(false);
      expect(validatePasswordChecks("a".repeat(30)).length).toBe(true);
    });

    it("should validate uppercase and lowercase", () => {
      expect(validatePasswordChecks("abcABC").case).toBe(true);
      expect(validatePasswordChecks("abcabc").case).toBe(false);
      expect(validatePasswordChecks("ABCABC").case).toBe(false);
    });

    it("should validate digits", () => {
      expect(validatePasswordChecks("abc123").number).toBe(true);
      expect(validatePasswordChecks("abcdef").number).toBe(false);
    });

    it("should validate special characters", () => {
      expect(validatePasswordChecks("abc!").special).toBe(true);
      expect(validatePasswordChecks("abc@#$").special).toBe(true);
      expect(validatePasswordChecks("abcdef").special).toBe(false);
    });

    it("should return all true for strong password", () => {
      expect(validatePasswordChecks("Test@1234")).toEqual({
        length: true,
        case: true,
        number: true,
        special: true,
      });
    });
  });

  // ─── calculateStrength ──────────────────────────────────────────────────────
  describe("calculateStrength", () => {
    it("should return 0 for no checks passing", () => {
      expect(calculateStrength({ length: false, case: false, number: false, special: false })).toBe(
        0,
      );
    });

    it("should return 25 for one check passing", () => {
      expect(calculateStrength({ length: true, case: false, number: false, special: false })).toBe(
        STRENGTH_MULTIPLIER,
      );
    });

    it("should return 100 for all checks passing", () => {
      expect(calculateStrength({ length: true, case: true, number: true, special: true })).toBe(
        STRENGTH_MULTIPLIER * 4,
      );
    });
  });

  // ─── areAllRequirementsMet ──────────────────────────────────────────────────
  describe("areAllRequirementsMet", () => {
    it("should return true when all checks pass", () => {
      expect(areAllRequirementsMet({ length: true, case: true, number: true, special: true })).toBe(
        true,
      );
    });

    it("should return false when any check fails", () => {
      expect(
        areAllRequirementsMet({ length: true, case: true, number: true, special: false }),
      ).toBe(false);
    });
  });

  // ─── getStrengthColor ──────────────────────────────────────────────────────
  describe("getStrengthColor", () => {
    it("should return WEAK color for strength <= 25", () => {
      expect(getStrengthColor(0)).toBe(STRENGTH_COLORS.WEAK);
      expect(getStrengthColor(STRENGTH_THRESHOLDS.WEAK)).toBe(STRENGTH_COLORS.WEAK);
    });

    it("should return MEDIUM_WEAK color for strength <= 50", () => {
      expect(getStrengthColor(26)).toBe(STRENGTH_COLORS.MEDIUM_WEAK);
      expect(getStrengthColor(STRENGTH_THRESHOLDS.MEDIUM)).toBe(STRENGTH_COLORS.MEDIUM_WEAK);
    });

    it("should return MEDIUM_STRONG color for strength <= 75", () => {
      expect(getStrengthColor(51)).toBe(STRENGTH_COLORS.MEDIUM_STRONG);
      expect(getStrengthColor(STRENGTH_THRESHOLDS.STRONG)).toBe(STRENGTH_COLORS.MEDIUM_STRONG);
    });

    it("should return STRONG color for strength > 75", () => {
      expect(getStrengthColor(76)).toBe(STRENGTH_COLORS.STRONG);
      expect(getStrengthColor(100)).toBe(STRENGTH_COLORS.STRONG);
    });
  });

  // ─── REGEX_PATTERNS ─────────────────────────────────────────────────────────
  describe("REGEX_PATTERNS", () => {
    it("should match lowercase letters", () => {
      expect(REGEX_PATTERNS.LOWERCASE.test("a")).toBe(true);
      expect(REGEX_PATTERNS.LOWERCASE.test("A")).toBe(false);
    });

    it("should match uppercase letters", () => {
      expect(REGEX_PATTERNS.UPPERCASE.test("A")).toBe(true);
      expect(REGEX_PATTERNS.UPPERCASE.test("a")).toBe(false);
    });

    it("should match digits", () => {
      expect(REGEX_PATTERNS.DIGIT.test("1")).toBe(true);
      expect(REGEX_PATTERNS.DIGIT.test("a")).toBe(false);
    });

    it("should match special characters", () => {
      expect(REGEX_PATTERNS.SPECIAL.test("!")).toBe(true);
      expect(REGEX_PATTERNS.SPECIAL.test("a")).toBe(false);
    });
  });
});
