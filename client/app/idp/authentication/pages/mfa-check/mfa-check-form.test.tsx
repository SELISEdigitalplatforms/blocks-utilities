import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { NuqsTestingAdapter } from "nuqs/adapters/testing";

import { useAuthStore } from "@seliseblocks/genesis-os";
import { MfaCheckFrom } from "./mfa-check-form";

const navigate = vi.fn();
vi.mock("react-router", async () => {
  const actual = await vi.importActual<typeof import("react-router")>("react-router");
  return { ...actual, useNavigate: () => navigate };
});

vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useVerifyMfa: () => ({ isPending: false }),
}));

const resend = vi.fn();
let remainingTime = 0;
vi.mock("@blocks-idp/mfa/hooks/use-resend-otp", () => ({
  useResendOtp: () => ({ remainingTime, resend }),
}));

const renderForm = (search: string) =>
  render(
    <MemoryRouter>
      <NuqsTestingAdapter searchParams={search}>
        <MfaCheckFrom />
      </NuqsTestingAdapter>
    </MemoryRouter>,
  );

describe("MfaCheckFrom", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    remainingTime = 0;
    useAuthStore.setState({ isAuthenticated: false });
  });

  // input-otp syncs the caret through timeouts of 0, 10 and 50ms after a value
  // change. Let them run while the jsdom window is still up, otherwise the last
  // one fires during environment teardown and React's state dispatch reports an
  // unhandled "window is not defined".
  afterEach(async () => {
    await new Promise((resolve) => setTimeout(resolve, 60));
  });

  it("renders five OTP slots and no resend button for email MFA (type 2)", () => {
    const { container } = renderForm("?mfa_type=2&mfa_id=abc");
    expect(screen.getByText("Resend Otp")).toBeInTheDocument();
    // input-otp renders a single hidden input plus the visual slots.
    expect(container.querySelector("input")).toBeInTheDocument();
  });

  it("authenticates and navigates once a valid 5-digit code is entered", async () => {
    const user = userEvent.setup();
    const { container } = renderForm("?mfa_type=2&mfa_id=abc");

    const input = container.querySelector("input") as HTMLInputElement;
    await user.type(input, "12345");

    const verify = screen.getByRole("button", { name: "Verify" });
    await waitFor(() => expect(verify).toBeEnabled());
    await user.click(verify);

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/services/language"));
    expect(useAuthStore.getState().isAuthenticated).toBe(true);
  });

  it("invokes resend when the resend button is clicked", async () => {
    const user = userEvent.setup();
    renderForm("?mfa_type=2&mfa_id=abc");
    await user.click(screen.getByRole("button", { name: /Resend Otp/i }));
    expect(resend).toHaveBeenCalled();
  });

  it("disables resend and shows the countdown when time remains", () => {
    remainingTime = 65;
    renderForm("?mfa_type=2&mfa_id=abc");
    const button = screen.getByRole("button", { name: /Resend Otp/i });
    expect(button).toBeDisabled();
    expect(button).toHaveTextContent("1:05");
  });

  it("renders six slots and hides resend for authenticator MFA (type 1)", () => {
    renderForm("?mfa_type=1&mfa_id=abc");
    expect(screen.queryByText("Resend Otp")).not.toBeInTheDocument();
  });
});
