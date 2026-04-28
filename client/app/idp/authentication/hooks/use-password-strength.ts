import { useState, useEffect, useCallback } from "react";
import {
  getPasswordRequirements,
  createInitialChecks,
  validatePasswordChecks,
  calculateStrength,
  areAllRequirementsMet,
  getStrengthColor,
} from "../utils/password-strength.util";
import type { PasswordChecks } from "../utils/password-strength.util";

export type { PasswordChecks, PasswordRequirement } from "../utils/password-strength.util";
export { getPasswordRequirements } from "../utils/password-strength.util";

export const usePasswordStrength = (password: string) => {
  const [strength, setStrength] = useState(0);
  const requirements = getPasswordRequirements();
  const [checks, setChecks] = useState<PasswordChecks>(createInitialChecks);

  const validatePassword = useCallback(() => {
    const newChecks = validatePasswordChecks(password);
    setChecks(newChecks);

    const strengthScore = calculateStrength(newChecks);
    setStrength(strengthScore);

    return areAllRequirementsMet(newChecks);
  }, [password]);

  useEffect(() => {
    validatePassword();
  }, [validatePassword]);

  return {
    strength,
    checks,
    allRequirementsMet: areAllRequirementsMet(checks),
    getStrengthColor: () => getStrengthColor(strength),
    requirements,
  };
};
