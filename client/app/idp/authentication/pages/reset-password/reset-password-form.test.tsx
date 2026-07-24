import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ResetPasswordForm } from "./reset-password-form";

const navigate = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual =
    await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return { ...actual, useNavigate: () => navigate };
});

const mutateAsync = vi.fn();
let isPending = false;
vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountResetPassword: () => ({ mutateAsync, isPending }),
}));

const resetCaptcha = vi.fn();
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: () => ({ captcha: {}, code: "captcha-code", reset: resetCaptcha }),
}));

vi.mock("@/components/captcha", () => ({
  Captcha: () => <div data-testid="captcha" />,
}));

const onRequirementsMetRef = { current: (_met: boolean) => {} };
vi.mock(
  "@blocks-idp/authentication/components/password-strength-checker/password-strength-checker",
  () => ({
    PasswordStrengthChecker: ({
      onRequirementsMet,
    }: {
      onRequirementsMet: (met: boolean) => void;
    }) => {
      onRequirementsMetRef.current = onRequirementsMet;
      return <div data-testid="strength" />;
    },
  }),
);

const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const renderForm = () =>
  render(
    <MemoryRouter>
      <ResetPasswordForm code="reset-code" />
    </MemoryRouter>,
  );

describe("ResetPasswordForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
  });

  it("renders the password fields and strength checker", () => {
    renderForm();
    expect(screen.getByLabelText("Password")).toBeInTheDocument();
    expect(screen.getByLabelText("Confirm Password")).toBeInTheDocument();
    expect(screen.getByTestId("strength")).toBeInTheDocument();
  });

  it("keeps the submit disabled until the form and requirements are satisfied", () => {
    renderForm();
    expect(
      screen.getByRole("button", { name: "Reset Password" }),
    ).toBeDisabled();
  });

  it("tracks typed password values", () => {
    renderForm();
    fireEvent.change(screen.getByLabelText("Password"), {
      target: { value: "Abcdef1!xy" },
    });
    fireEvent.change(screen.getByLabelText("Confirm Password"), {
      target: { value: "Abcdef1!xy" },
    });
    expect(screen.getByLabelText("Password")).toHaveValue("Abcdef1!xy");
    expect(screen.getByLabelText("Confirm Password")).toHaveValue("Abcdef1!xy");
  });
});
