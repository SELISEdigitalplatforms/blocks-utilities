import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { OidcForgotPasswordForm } from "./oidc-forgot-password-form";

const navigate = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual =
    await vi.importActual<typeof import("react-router-dom")>(
      "react-router-dom",
    );
  return { ...actual, useNavigate: () => navigate };
});

vi.mock("@/layouts/oidc-layout", () => ({
  useOIDCContext: () => ({ themeColor: "#124091", projectKey: "proj-1" }),
}));

const accountRecover = vi.fn();
vi.mock("@blocks-idp/authentication/services/oidc-auth-flow.service", () => ({
  accountRecover: (...a: unknown[]) => accountRecover(...a),
}));

const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const renderForm = () =>
  render(
    <MemoryRouter>
      <OidcForgotPasswordForm />
    </MemoryRouter>,
  );

describe("OidcForgotPasswordForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders the email field, continue button and login link", () => {
    renderForm();
    expect(screen.getByPlaceholderText("Enter your email")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Continue" })).toBeDisabled();
    expect(screen.getByRole("link", { name: "Log in" })).toBeInTheDocument();
  });

  it("recovers the account and navigates to the confirmation page", async () => {
    accountRecover.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    renderForm();

    await user.type(
      screen.getByPlaceholderText("Enter your email"),
      "user@test.com",
    );
    const submit = screen.getByRole("button", { name: "Continue" });
    await waitFor(() => expect(submit).toBeEnabled());
    await user.click(submit);

    await waitFor(() =>
      expect(accountRecover).toHaveBeenCalledWith({
        email: "user@test.com",
        projectKey: "proj-1",
      }),
    );
    await waitFor(() => expect(navigate).toHaveBeenCalled());
    expect(navigate.mock.calls[0][0]).toContain(
      "email=" + encodeURIComponent("user@test.com"),
    );
  });

  it("shows an error toast when the recovery is unsuccessful", async () => {
    accountRecover.mockResolvedValue({ isSuccess: false, error: "no user" });
    const user = userEvent.setup();
    renderForm();

    await user.type(
      screen.getByPlaceholderText("Enter your email"),
      "user@test.com",
    );
    const submit = screen.getByRole("button", { name: "Continue" });
    await waitFor(() => expect(submit).toBeEnabled());
    await user.click(submit);

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "no user" }),
    );
    expect(navigate).not.toHaveBeenCalled();
  });
});
