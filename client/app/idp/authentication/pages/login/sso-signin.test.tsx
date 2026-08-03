import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { SsoSignin } from "./sso-signin";

let ssoActivationState: { isPending: boolean };
vi.mock("@blocks-idp/authentication/hooks/use-sso-activation", () => ({
  useSsoActivation: () => ssoActivationState,
}));

vi.mock("@blocks-idp/authentication/components/sso-signin-card", () => ({
  SSOSigninCard: ({
    providerConfig,
  }: {
    providerConfig: { provider: string };
  }) => <div data-testid="sso-card">{providerConfig.provider}</div>,
}));

vi.mock("@/components/loader-spinner/loader-spinner", () => ({
  default: () => <div data-testid="spinner" />,
}));

describe("SsoSignin", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    ssoActivationState = { isPending: false };
  });

  it("renders a card only for available configured providers", () => {
    const loginOption = {
      ssoInfo: [{ provider: "google", audience: "aud" }],
    } as never;
    render(<SsoSignin loginOption={loginOption} />);
    const cards = screen.getAllByTestId("sso-card");
    expect(cards.length).toBeGreaterThanOrEqual(1);
    expect(cards.some((c) => c.textContent === "google")).toBe(true);
  });

  it("renders no cards when there is no sso info", () => {
    render(<SsoSignin loginOption={{ ssoInfo: [] } as never} />);
    expect(screen.queryByTestId("sso-card")).not.toBeInTheDocument();
  });

  it("shows the loading spinner while activation is pending", () => {
    ssoActivationState = { isPending: true };
    render(<SsoSignin loginOption={{ ssoInfo: [] } as never} />);
    expect(screen.getByTestId("spinner")).toBeInTheDocument();
  });
});
