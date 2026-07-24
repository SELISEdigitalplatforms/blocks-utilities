import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { Activation } from "./activation";

const activationCodeValidation = vi.fn();
let isActivationPending = false;
const resendActivationLink = vi.fn();
let isResendPending = false;
vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountActivationCodeExpiration: () => ({
    mutateAsync: activationCodeValidation,
    isPending: isActivationPending,
  }),
  useAccountResendActivation: () => ({
    mutateAsync: resendActivationLink,
    isPending: isResendPending,
  }),
}));

vi.mock("./activation-form", () => ({
  ActivationForm: ({ code }: { code: string }) => (
    <div data-testid="activation-form">code:{code}</div>
  ),
}));
vi.mock("@/components/logo", () => ({ Logo: () => <div data-testid="logo" /> }));

const renderActivation = (code?: string) =>
  render(
    <MemoryRouter>
      <Activation code={code} />
    </MemoryRouter>,
  );

describe("Activation", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isActivationPending = false;
    isResendPending = false;
  });

  it("shows the invalid link message when no code is provided", async () => {
    renderActivation(undefined);
    expect(await screen.findByText("Invalid Activation Link")).toBeInTheDocument();
  });

  it("renders the activation form for a valid code", async () => {
    activationCodeValidation.mockResolvedValue({ isSuccess: true, errors: null, userId: null });
    renderActivation("good-code");
    expect(await screen.findByTestId("activation-form")).toHaveTextContent(
      "code:good-code",
    );
  });

  it("shows the invalid state when validation returns errors", async () => {
    activationCodeValidation.mockResolvedValue({ isSuccess: false, errors: ["nope"] });
    renderActivation("bad-code");
    expect(await screen.findByText("Invalid Activation Link")).toBeInTheDocument();
  });

  it("shows the expired state and resends the activation link", async () => {
    activationCodeValidation.mockResolvedValue({
      isSuccess: true,
      errors: null,
      userId: "u1",
    });
    resendActivationLink.mockResolvedValue({ isSuccess: true });
    renderActivation("expired-code");
    const resend = await screen.findByRole("button", {
      name: "Resend activation link",
    });
    fireEvent.click(resend);
    await waitFor(() =>
      expect(
        screen.getByText("A new activation link has been sent to your email."),
      ).toBeInTheDocument(),
    );
  });

  it("shows a validating message while the code check is pending", () => {
    isActivationPending = true;
    renderActivation("code");
    expect(screen.getByText("Validating activation code...")).toBeInTheDocument();
  });
});
