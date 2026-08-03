import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { PasswordStrengthChecker } from "./password-strength-checker";

const strongPassword = "Str0ng!Passw0rd";

describe("PasswordStrengthChecker", () => {
  it("renders the requirement list and a passwords-match row", () => {
    render(
      <PasswordStrengthChecker
        password=""
        confirmPassword=""
        onRequirementsMet={vi.fn()}
      />,
    );
    expect(screen.getByText("Password Requirements")).toBeInTheDocument();
    expect(screen.getByText("Passwords match")).toBeInTheDocument();
  });

  it("reports all requirements met for a strong matching password", () => {
    const onRequirementsMet = vi.fn();
    render(
      <PasswordStrengthChecker
        password={strongPassword}
        confirmPassword={strongPassword}
        onRequirementsMet={onRequirementsMet}
      />,
    );
    expect(onRequirementsMet).toHaveBeenLastCalledWith(true);
  });

  it("reports requirements not met when passwords differ", () => {
    const onRequirementsMet = vi.fn();
    render(
      <PasswordStrengthChecker
        password={strongPassword}
        confirmPassword="different"
        onRequirementsMet={onRequirementsMet}
      />,
    );
    expect(onRequirementsMet).toHaveBeenLastCalledWith(false);
  });

  it("shows the exclude-password row and fails when equal to the excluded value", () => {
    const onRequirementsMet = vi.fn();
    render(
      <PasswordStrengthChecker
        password={strongPassword}
        confirmPassword={strongPassword}
        excludePassword={strongPassword}
        excludePasswordLabel="Must differ from current"
        onRequirementsMet={onRequirementsMet}
      />,
    );
    expect(screen.getByText("Must differ from current")).toBeInTheDocument();
    expect(onRequirementsMet).toHaveBeenLastCalledWith(false);
  });

  it("passes the exclude check when the new password differs", () => {
    const onRequirementsMet = vi.fn();
    render(
      <PasswordStrengthChecker
        password={strongPassword}
        confirmPassword={strongPassword}
        excludePassword="Old!Passw0rd1"
        onRequirementsMet={onRequirementsMet}
      />,
    );
    expect(
      screen.getByText("New password shouldn't match current password"),
    ).toBeInTheDocument();
    expect(onRequirementsMet).toHaveBeenLastCalledWith(true);
  });
});
