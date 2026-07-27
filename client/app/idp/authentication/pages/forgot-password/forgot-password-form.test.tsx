import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ForgotPasswordForm } from "./forgot-password-form";

const navigate = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual =
    await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return { ...actual, useNavigate: () => navigate };
});

const mutateAsync = vi.fn();
let isPending = false;
vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountRecover: () => ({ mutateAsync, isPending }),
}));

const resetCaptcha = vi.fn();
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: () => ({ captcha: {}, code: "captcha-code", reset: resetCaptcha }),
}));
vi.mock("@/components/captcha", () => ({ Captcha: () => <div data-testid="captcha" /> }));

const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const renderForm = () =>
  render(
    <MemoryRouter>
      <ForgotPasswordForm />
    </MemoryRouter>,
  );

describe("ForgotPasswordForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
  });

  it("renders the email field", () => {
    renderForm();
    expect(screen.getByLabelText("Email")).toBeInTheDocument();
  });

  it("recovers and navigates on success", async () => {
    mutateAsync.mockResolvedValue({ isSuccess: true });
    renderForm();
    fireEvent.change(screen.getByLabelText("Email"), {
      target: { value: "user@example.com" },
    });
    const submit = screen.getByRole("button", { name: "Continue" });
    await waitFor(() => expect(submit).toBeEnabled());
    fireEvent.click(submit);
    await waitFor(() => expect(mutateAsync).toHaveBeenCalled());
    expect(navigate).toHaveBeenCalledWith(
      "/forgot-email-sent?email=user@example.com",
    );
  });

  it("shows an error toast when recovery is not successful", async () => {
    mutateAsync.mockResolvedValue({ isSuccess: false, errors: ["bad"] });
    renderForm();
    fireEvent.change(screen.getByLabelText("Email"), {
      target: { value: "user@example.com" },
    });
    const submit = screen.getByRole("button", { name: "Continue" });
    await waitFor(() => expect(submit).toBeEnabled());
    fireEvent.click(submit);
    await waitFor(() => expect(showErrorToast).toHaveBeenCalledWith({ errors: ["bad"] }));
    expect(resetCaptcha).toHaveBeenCalled();
  });
});
