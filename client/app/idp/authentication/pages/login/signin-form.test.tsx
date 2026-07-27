import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { useAuthStore } from "@seliseblocks/blocks-kit";
import { SigninForm } from "./signin-form";

const navigate = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual =
    await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return { ...actual, useNavigate: () => navigate };
});

const mutateAsync = vi.fn();
let isPending = false;
vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useSigninByEmail: () => ({ mutateAsync, isPending }),
}));

const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

vi.mock("@/components/captcha", () => ({
  Captcha: () => <div data-testid="captcha" />,
}));

const renderForm = () =>
  render(
    <MemoryRouter>
      <SigninForm />
    </MemoryRouter>,
  );

describe("SigninForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
    useAuthStore.getState().setUnAuthenticated?.();
  });

  it("renders the email and password fields", () => {
    renderForm();
    expect(screen.getByLabelText("Email")).toBeInTheDocument();
    expect(screen.getByLabelText("Password")).toBeInTheDocument();
  });

  it("shows a validation error for an invalid email", async () => {
    renderForm();
    fireEvent.change(screen.getByLabelText("Email"), {
      target: { value: "notanemail" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    expect(await screen.findByText("Invalid email format")).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("authenticates and navigates on a successful sign in", async () => {
    mutateAsync.mockResolvedValue({ enable_mfa: false, access_token: "a", refresh_token: "r" });
    renderForm();
    fireEvent.change(screen.getByLabelText("Email"), {
      target: { value: "user@example.com" },
    });
    fireEvent.change(screen.getByLabelText("Password"), {
      target: { value: "secret" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() => expect(mutateAsync).toHaveBeenCalled());
    expect(navigate).toHaveBeenCalledWith("/services/language");
  });

  it("redirects to the MFA check when MFA is required", async () => {
    mutateAsync.mockResolvedValue({ enable_mfa: true, mfaId: "m1", mfaType: 2 });
    renderForm();
    fireEvent.change(screen.getByLabelText("Email"), {
      target: { value: "user@example.com" },
    });
    fireEvent.change(screen.getByLabelText("Password"), {
      target: { value: "secret" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() =>
      expect(navigate).toHaveBeenCalledWith("/mfa-check?mfa_id=m1&mfa_type=2"),
    );
  });

  it("shows an error toast when the request throws", async () => {
    mutateAsync.mockRejectedValue({ errors: { error_description: "bad creds" } });
    renderForm();
    fireEvent.change(screen.getByLabelText("Email"), {
      target: { value: "user@example.com" },
    });
    fireEvent.change(screen.getByLabelText("Password"), {
      target: { value: "secret" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({ errors: "bad creds" }),
    );
  });
});
