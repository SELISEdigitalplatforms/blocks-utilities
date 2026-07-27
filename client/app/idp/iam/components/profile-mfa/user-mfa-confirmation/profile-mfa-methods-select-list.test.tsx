import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { profileMfaContext } from "../profile-mfa";
import { ProfileMfaMethodSelectList } from "./profile-mfa-methods-select-list";

let mfaConfigData: unknown;
vi.mock("@/idp/mfa/hooks/use-mfa-config", () => ({
  useGetMFAConfig: () => ({ data: mfaConfigData }),
}));

let meData: unknown;
vi.mock("@/idp/iam/hooks/use-user", () => ({
  useGetMe: () => ({ data: meData }),
}));

vi.mock("./profile-mfa-veriffy/profile-mfa-verify", () => ({
  ProfileMFAVerify: () => <div data-testid="verify-modal" />,
}));
vi.mock("./profile-mfa-confirmation-disable", () => ({
  UserMFAConfirmationDisable: () => <div data-testid="disable-modal" />,
}));

const showVerifyModal = vi.fn();
const setIsDisableModalOpen = vi.fn();

const renderWithContext = () =>
  render(
    <profileMfaContext.Provider
      value={
        {
          userId: "u1",
          projectKey: "tg-1",
          showVerifyModal,
          setIsDisableModalOpen,
        } as never
      }
    >
      <ProfileMfaMethodSelectList />
    </profileMfaContext.Provider>,
  );

describe("ProfileMfaMethodSelectList", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mfaConfigData = { userMfaType: [1, 2] };
    meData = { data: { userMfaType: 0, isMfaVerified: false } };
  });

  it("renders the None option plus every available method", () => {
    renderWithContext();
    expect(screen.getByText("None")).toBeInTheDocument();
    expect(screen.getByText("Email")).toBeInTheDocument();
    expect(screen.getByText("Authenticator app")).toBeInTheDocument();
  });

  it("opens the verify modal when a method is enabled", async () => {
    const user = userEvent.setup();
    renderWithContext();
    // The Email method (type 2) shows an Enable button.
    const enableButtons = screen.getAllByRole("button", { name: "Enable" });
    await user.click(enableButtons[0]);
    expect(showVerifyModal).toHaveBeenCalled();
  });

  it("opens the disable modal when None is selected", async () => {
    // Make an authenticator method active so None is not the active row.
    meData = { data: { userMfaType: 1, isMfaVerified: true } };
    const user = userEvent.setup();
    renderWithContext();
    await user.click(screen.getByRole("button", { name: "Disable" }));
    expect(setIsDisableModalOpen).toHaveBeenCalledWith(true);
  });

  it("renders no methods when none are configured", () => {
    mfaConfigData = { userMfaType: [] };
    renderWithContext();
    expect(screen.getByText("None")).toBeInTheDocument();
    expect(screen.queryByText("Email")).not.toBeInTheDocument();
  });
});
