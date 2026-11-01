import { tStatic } from "@seliseblocks/genesis-os";

const UTILITIES_IDP_AUTHENTICATION_MODULE = "utilities.idp.authentication";

const PASSWORD_MIN_LENGTH = 8;
const PASSWORD_MAX_LENGTH = 30;
export const STRENGTH_MULTIPLIER = 25;
export const STRENGTH_THRESHOLDS = {
  WEAK: 25,
  MEDIUM: 50,
  STRONG: 75,
} as const;

export const STRENGTH_COLORS = {
  WEAK: "bg-red-500",
  MEDIUM_WEAK: "bg-orange-500",
  MEDIUM_STRONG: "bg-yellow-500",
  STRONG: "bg-green-600",
} as const;

export const REGEX_PATTERNS = {
  LOWERCASE: /[a-z]/,
  UPPERCASE: /[A-Z]/,
  DIGIT: /\d/,
  SPECIAL: /[^A-Za-z0-9\s]/,
} as const;

export interface PasswordChecks {
  length: boolean;
  case: boolean;
  number: boolean;
  special: boolean;
}

export interface PasswordRequirement {
  key: keyof PasswordChecks;
  label: string;
}


export const getPasswordRequirements = (): PasswordRequirement[] => [
  { key: "length", label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "PASSWORD_STRENGTH_UTIL.BETWEEN_8_AND_30_CHARACTERS") },
  { key: "case", label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "PASSWORD_STRENGTH_UTIL.AT_LEAST_1_UPPERCASE_AND") },
  { key: "number", label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "PASSWORD_STRENGTH_UTIL.AT_LEAST_1_DIGIT") },
  {
    key: "special",
    label: tStatic(UTILITIES_IDP_AUTHENTICATION_MODULE, "PASSWORD_STRENGTH_UTIL.AT_LEAST_1_SPECIAL_CHARACTER"),
  },
];

export const createInitialChecks = (): PasswordChecks => ({
  length: false,
  case: false,
  number: false,
  special: false,
});

export const validatePasswordChecks = (password: string): PasswordChecks => ({
  length: password.length >= PASSWORD_MIN_LENGTH && password.length <= PASSWORD_MAX_LENGTH,
  case: REGEX_PATTERNS.LOWERCASE.test(password) && REGEX_PATTERNS.UPPERCASE.test(password),
  number: REGEX_PATTERNS.DIGIT.test(password),
  special: REGEX_PATTERNS.SPECIAL.test(password),
});

export const calculateStrength = (checks: PasswordChecks): number =>
  Object.values(checks).filter(Boolean).length * STRENGTH_MULTIPLIER;

export const areAllRequirementsMet = (checks: PasswordChecks): boolean =>
  Object.values(checks).every(Boolean);

export const getStrengthColor = (strength: number): string => {
  if (strength <= STRENGTH_THRESHOLDS.WEAK) return STRENGTH_COLORS.WEAK;
  if (strength <= STRENGTH_THRESHOLDS.MEDIUM) return STRENGTH_COLORS.MEDIUM_WEAK;
  if (strength <= STRENGTH_THRESHOLDS.STRONG) return STRENGTH_COLORS.MEDIUM_STRONG;
  return STRENGTH_COLORS.STRONG;
};
