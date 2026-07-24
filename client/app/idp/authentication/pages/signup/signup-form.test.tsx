import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { SignupForm } from "./signup-form";

const navigate = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual =
    await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return { ...actual, useNavigate: () => navigate };
});

const mutateAsync = vi.fn();
let isPending = false;
vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useSignupByEmail: () => ({ mutateAsync, isPending }),
}));

const resetCaptcha = vi.fn();
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: () => ({ captcha: {}, code: "captcha-code", reset: resetCaptcha }),
}));
vi.mock("@/components/captcha", () => ({ Captcha: () => <div data-testid="captcha" /> }));
vi.mock("../login/sso-signin", () => ({
  SsoSignin: () => <div data-testid="sso-signin" />,
}));

const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const loginOption = { allowedGrantTypes: ["social"] } as never;

const renderForm = (props: Partial<Record<string, unknown>> = {}) =>
  render(
    <MemoryRouter>
      <SignupForm
        loginOption={loginOption}
        emailSignUpEnabled
        ssoSignUpEnabled={false}
        {...(props as never)}
      />
    </MemoryRouter>,
  );

describe("SignupForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
  });

  it("renders the email field and terms agreement", () => {
    renderForm();
    expect(screen.getByLabelText("Email")).toBeInTheDocument();
    expect(screen.getByText(/I agree to the/)).toBeInTheDocument();
  });

  it("keeps Continue disabled until valid, captcha and terms are set", async () => {
    renderForm();
    const submit = screen.getByRole("button", { name: "Continue" });
    expect(submit).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Email"), {
      target: { value: "user@example.com" },
    });
    fireEvent.click(screen.getByRole("checkbox"));
    await waitFor(() => expect(submit).toBeEnabled());
  });

  it("signs up and navigates on success", async () => {
    mutateAsync.mockResolvedValue({ isSuccess: true });
    renderForm();
    fireEvent.change(screen.getByLabelText("Email"), {
      target: { value: "user@example.com" },
    });
    fireEvent.click(screen.getByRole("checkbox"));
    const submit = screen.getByRole("button", { name: "Continue" });
    await waitFor(() => expect(submit).toBeEnabled());
    fireEvent.click(submit);
    await waitFor(() => expect(mutateAsync).toHaveBeenCalled());
    expect(navigate).toHaveBeenCalledWith(
      "/signup-email-sent?email=user@example.com",
    );
  });

  it("shows an error toast when signup is not successful", async () => {
    mutateAsync.mockResolvedValue({ isSuccess: false, errors: ["bad"] });
    renderForm();
    fireEvent.change(screen.getByLabelText("Email"), {
      target: { value: "user@example.com" },
    });
    fireEvent.click(screen.getByRole("checkbox"));
    const submit = screen.getByRole("button", { name: "Continue" });
    await waitFor(() => expect(submit).toBeEnabled());
    fireEvent.click(submit);
    await waitFor(() => expect(showErrorToast).toHaveBeenCalledWith({ errors: ["bad"] }));
  });

  it("renders the SSO section when enabled", () => {
    renderForm({ ssoSignUpEnabled: true });
    expect(screen.getByTestId("sso-signin")).toBeInTheDocument();
  });
});
