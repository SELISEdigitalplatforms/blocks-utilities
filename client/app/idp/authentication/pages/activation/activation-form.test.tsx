import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { ActivationForm } from "./activation-form";

const navigate = vi.fn();
vi.mock("react-router", async () => {
  const actual =
    await vi.importActual<typeof import("react-router")>("react-router");
  return { ...actual, useNavigate: () => navigate };
});

const mutateAsync = vi.fn();
let isPending = false;
vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountActivation: () => ({ mutateAsync, isPending }),
}));

const resetCaptcha = vi.fn();
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: () => ({ captcha: {}, code: "captcha-code", reset: resetCaptcha }),
}));
vi.mock("@/components/captcha", () => ({ Captcha: () => <div data-testid="captcha" /> }));
vi.mock(
  "../../components/password-strength-checker/password-strength-checker",
  () => ({
    PasswordStrengthChecker: ({
      onRequirementsMet,
    }: {
      onRequirementsMet: (met: boolean) => void;
    }) => {
      onRequirementsMet(true);
      return <div data-testid="strength" />;
    },
  }),
);

const showErrorToast = vi.fn();
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (...a: unknown[]) => showErrorToast(...a),
}));

const renderForm = (code = "act-code") =>
  render(
    <MemoryRouter>
      <ActivationForm code={code} />
    </MemoryRouter>,
  );

const fillValid = () => {
  fireEvent.change(screen.getByLabelText("First Name"), { target: { value: "Ada" } });
  fireEvent.change(screen.getByLabelText("Last Name"), { target: { value: "Lovelace" } });
  fireEvent.change(screen.getByLabelText("Password"), { target: { value: "Abcdef1!xy" } });
  fireEvent.change(screen.getByLabelText("Confirm Password"), {
    target: { value: "Abcdef1!xy" },
  });
};

describe("ActivationForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isPending = false;
  });

  it("renders all fields", () => {
    renderForm();
    expect(screen.getByLabelText("First Name")).toBeInTheDocument();
    expect(screen.getByLabelText("Last Name")).toBeInTheDocument();
    expect(screen.getByLabelText("Password")).toBeInTheDocument();
    expect(screen.getByLabelText("Confirm Password")).toBeInTheDocument();
  });

  it("redirects to login when no code is present", () => {
    renderForm("");
    expect(navigate).toHaveBeenCalledWith("/login");
  });

  it("activates and navigates to success", async () => {
    mutateAsync.mockResolvedValue({ isSuccess: true });
    renderForm();
    fillValid();
    const submit = screen.getByRole("button", { name: "Activate BTN" });
    await waitFor(() => expect(submit).toBeEnabled());
    fireEvent.click(submit);
    await waitFor(() => expect(mutateAsync).toHaveBeenCalled());
    expect(mutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({
        code: "act-code",
        firstname: "Ada",
        lastname: "Lovelace",
        password: "Abcdef1!xy",
      }),
    );
    expect(navigate).toHaveBeenCalledWith("/activate-success");
  });

  it("shows an error toast when activation is not successful", async () => {
    mutateAsync.mockResolvedValue({ isSuccess: false, errors: ["bad"] });
    renderForm();
    fillValid();
    const submit = screen.getByRole("button", { name: "Activate BTN" });
    await waitFor(() => expect(submit).toBeEnabled());
    fireEvent.click(submit);
    await waitFor(() => expect(showErrorToast).toHaveBeenCalledWith({ errors: ["bad"] }));
    expect(resetCaptcha).toHaveBeenCalled();
  });
});
