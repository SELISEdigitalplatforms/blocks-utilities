import React, { useState, useEffect } from "react";
import { Check, X } from "lucide-react";
import { usePasswordStrength } from "@blocks-idp/authentication/hooks/use-password-strength";

interface PasswordStrengthCheckerProps {
  password: string;
  confirmPassword: string;
  onRequirementsMet: (met: boolean) => void;
  excludePassword?: string;
  excludePasswordLabel?: string;
}

export const PasswordStrengthChecker: React.FC<PasswordStrengthCheckerProps> = ({
  password,
  confirmPassword,
  onRequirementsMet,
  excludePassword,
  excludePasswordLabel,
}) => {
  const { checks, requirements } = usePasswordStrength(password);
  const [passwordsMatch, setPasswordsMatch] = useState(false);
  const [isDifferentFromExcluded, setIsDifferentFromExcluded] = useState(true);

  useEffect(() => {
    setPasswordsMatch(password === confirmPassword && password !== "");

    if (excludePassword && password !== "") {
      setIsDifferentFromExcluded(password !== excludePassword);
    } else {
      setIsDifferentFromExcluded(true);
    }

    const allMet =
      Object.values(checks).every((check) => check) && passwordsMatch && isDifferentFromExcluded;
    onRequirementsMet(allMet);
  }, [
    password,
    confirmPassword,
    checks,
    passwordsMatch,
    excludePassword,
    isDifferentFromExcluded,
    onRequirementsMet,
  ]);

  const getAdjustedStrength = () => {
    const basicRequirements = Object.values(checks).filter(Boolean).length;
    let totalRequirements = Object.keys(checks).length;
    let metRequirements = basicRequirements;

    totalRequirements += 1;
    if (passwordsMatch) {
      metRequirements += 1;
    }

    if (excludePassword && password !== "") {
      totalRequirements += 1;
      if (isDifferentFromExcluded) {
        metRequirements += 1;
      }
    }

    return totalRequirements > 0 ? (metRequirements / totalRequirements) * 100 : 0;
  };

  const getAdjustedStrengthColor = () => {
    const adjustedStrength = getAdjustedStrength();
    if (adjustedStrength <= 25) return "bg-red-500";
    if (adjustedStrength <= 50) return "bg-orange-500";
    if (adjustedStrength <= 75) return "bg-yellow-500";
    return "bg-green-600";
  };

  return (
    <div className="border-primary-50 mx-auto w-full rounded-lg border px-6 py-4 shadow-sm">
      <h2 className="mb-2 text-sm font-semibold text-high-emphasis">Password Requirements</h2>
      <div className="bg-primary-50 mb-2 h-1 w-full rounded">
        <div
          className={`h-1 rounded-full transition-all duration-300 ${getAdjustedStrengthColor()}`}
          style={{ width: `${getAdjustedStrength()}%` }}
        />
      </div>

      <p className="mb-2 text-xs text-medium-emphasis">
        Your password must meet these requirements:
      </p>

      <ul className="space-y-1 text-xs text-medium-emphasis">
        {requirements.map((requirement) => (
          <li key={requirement.key} className="flex items-center gap-2">
            {checks[requirement.key] ? (
              <Check className="h-4 w-4 text-success" />
            ) : (
              <X className="h-4 w-4 text-red-500" />
            )}
            {requirement.label}
          </li>
        ))}

        {excludePassword && password !== "" && (
          <li className="flex items-center gap-2">
            {isDifferentFromExcluded ? (
              <Check className="h-4 w-4 text-success" />
            ) : (
              <X className="h-4 w-4 text-red-500" />
            )}
            {excludePasswordLabel ?? "New password shouldn't match current password"}
          </li>
        )}

        <li className="flex items-center gap-2">
          {passwordsMatch ? (
            <Check className="h-4 w-4 text-success" />
          ) : (
            <X className="h-4 w-4 text-red-500" />
          )}
          Passwords match
        </li>
      </ul>
    </div>
  );
};
