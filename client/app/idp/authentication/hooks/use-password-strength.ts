import {
  getPasswordRequirements,
  validatePasswordChecks,
  calculateStrength,
  areAllRequirementsMet,
  getStrengthColor,
} from "../utils/password-strength.util";

export type { PasswordChecks, PasswordRequirement } from "../utils/password-strength.util";
export { getPasswordRequirements } from "../utils/password-strength.util";

export const usePasswordStrength = (password: string) => {
  const requirements = getPasswordRequirements();
  const checks = validatePasswordChecks(password);
  const strength = calculateStrength(checks);

  return {
    strength,
    checks,
    allRequirementsMet: areAllRequirementsMet(checks),
    getStrengthColor: () => getStrengthColor(strength),
    requirements,
  };
};
