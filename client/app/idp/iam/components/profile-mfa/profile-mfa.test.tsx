import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { ProfileMFA, ProjectMFA, ProfileConfigMFA } from "./profile-mfa";

let mfaConfigState: { isLoading: boolean; data: unknown };
vi.mock("@/idp/mfa/hooks/use-mfa-config", () => ({
  useGetMFAConfig: () => mfaConfigState,
}));

vi.mock("./profile-mfa-detail", () => ({
  ProfileMFADetails: () => <div data-testid="mfa-details" />,
}));
vi.mock("./user-mfa-confirmation/profile-mfa-methods-select-list", () => ({
  ProfileMfaMethodSelectList: () => <div data-testid="mfa-methods" />,
}));

const renderRouted = (ui: React.ReactElement) =>
  render(<MemoryRouter>{ui}</MemoryRouter>);

describe("ProfileMFA", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mfaConfigState = { isLoading: false, data: { enableMfa: true } };
  });

  it("shows the loading skeleton while the config loads", () => {
    mfaConfigState = { isLoading: true, data: undefined };
    const { container } = renderRouted(
      <ProfileMFA userId="u1" projectKey="tg-1" />,
    );
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(
      0,
    );
  });

  it("shows the project MFA prompt when MFA is not enabled", () => {
    mfaConfigState = { isLoading: false, data: { enableMfa: false } };
    renderRouted(<ProfileMFA userId="u1" projectKey="tg-1" />);
    expect(
      screen.getByRole("link", { name: "Go to MFA Settings" }),
    ).toBeInTheDocument();
  });

  it("renders the config view with details and method list when enabled", () => {
    renderRouted(<ProfileMFA userId="u1" projectKey="tg-1" />);
    expect(screen.getByText("Multi-factor Authentication")).toBeInTheDocument();
    expect(screen.getByTestId("mfa-details")).toBeInTheDocument();
    expect(screen.getByTestId("mfa-methods")).toBeInTheDocument();
  });

  it("ProjectMFA renders the settings link and explanation", () => {
    renderRouted(<ProjectMFA />);
    expect(screen.getByText(/enhances your account security/)).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Go to MFA Settings" }),
    ).toBeInTheDocument();
  });

  it("ProfileConfigMFA renders its title", () => {
    renderRouted(<ProfileConfigMFA />);
    expect(screen.getByText("Multi-factor Authentication")).toBeInTheDocument();
    expect(screen.getByTestId("mfa-methods")).toBeInTheDocument();
  });
});
