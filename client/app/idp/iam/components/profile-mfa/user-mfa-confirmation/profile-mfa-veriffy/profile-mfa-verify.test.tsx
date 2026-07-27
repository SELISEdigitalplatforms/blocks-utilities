import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { profileMfaContext } from "../../profile-mfa";
import { ProfileMFAVerify } from "./profile-mfa-verify";

const generateOtp = vi.fn();
vi.mock("@/idp/mfa/hooks/use-mfa-config", () => ({
  useGenerateUserMfaOTP: () => ({ mutateAsync: generateOtp }),
}));

vi.mock("./profile-mfa-verify-form", () => ({
  ProfileMfaVerifyForm: ({ mfaId }: { mfaId: string }) => (
    <div data-testid="verify-form">{mfaId}</div>
  ),
}));
vi.mock("./profile-mfa-verify-guideline-totp", () => ({
  ProfileMfaVerifyGuideLineTotp: () => <div data-testid="totp-guide" />,
}));
vi.mock("./profile-mfa-verify-guideline-email", () => ({
  ProfileMfaVerifyGuideLineEmail: () => <div data-testid="email-guide" />,
}));

const renderWithContext = (overrides: Record<string, unknown> = {}) =>
  render(
    <profileMfaContext.Provider
      value={
        {
          userId: "u1",
          projectKey: "tg-1",
          isVerifyModalOpen: true,
          setIsVerifyModalOpen: vi.fn(),
          mfaMethodType: 1,
          ...overrides,
        } as never
      }
    >
      <ProfileMFAVerify />
    </profileMfaContext.Provider>,
  );

describe("ProfileMFAVerify", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    generateOtp.mockResolvedValue({ isSuccess: true, mfaId: "mfa-123" });
  });

  it("generates an OTP and shows the totp guideline for authenticator type", async () => {
    renderWithContext({ mfaMethodType: 1 });
    await waitFor(() =>
      expect(generateOtp).toHaveBeenCalledWith({
        projectKey: "tg-1",
        userId: "u1",
        mfaType: 1,
      }),
    );
    expect(
      screen.getByText("Set up your authenticator app"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("totp-guide")).toBeInTheDocument();
  });

  it("shows the email guideline for the email type and passes the mfaId", async () => {
    renderWithContext({ mfaMethodType: 2 });
    await waitFor(() => expect(generateOtp).toHaveBeenCalled());
    expect(screen.getByTestId("email-guide")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByTestId("verify-form")).toHaveTextContent("mfa-123"),
    );
  });

  it("does not generate an OTP while the modal is closed", () => {
    renderWithContext({ isVerifyModalOpen: false });
    expect(generateOtp).not.toHaveBeenCalled();
  });
});
