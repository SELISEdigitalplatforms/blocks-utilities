import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { SSOProviderList } from "./sso-provider-list";

let authConfigState: { isLoading: boolean };
let ssoState: { data: unknown };
vi.mock("@blocks-idp/authentication/hooks/use-auth-config", () => ({
  useGetAuthConfig: () => authConfigState,
}));
vi.mock("@blocks-idp/authentication/hooks/use-sso", () => ({
  useGetSsoCredentials: () => ssoState,
}));

vi.mock(
  "@blocks-idp/authentication/components/sso-provider-card/sso-provider-card",
  () => ({
    SSOProviderCard: ({
      configuration,
    }: {
      configuration: { provider: string };
    }) => <div data-testid="sso-card">{configuration.provider}</div>,
    SSOProviderCardSkelton: () => <div data-testid="sso-skeleton" />,
  }),
);

describe("SSOProviderList", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authConfigState = { isLoading: false };
    ssoState = { data: [] };
    useProjectStore.setState({ selectedProject: { tenantId: "tg-1" } });
  });

  it("shows skeletons while the auth config loads", () => {
    authConfigState.isLoading = true;
    render(<SSOProviderList />);
    expect(screen.getAllByTestId("sso-skeleton").length).toBe(6);
  });

  it("renders a provider card for each known provider", () => {
    render(<SSOProviderList />);
    const cards = screen.getAllByTestId("sso-card");
    expect(cards.length).toBeGreaterThan(0);
  });

  it("merges configured credentials into the provider list", () => {
    ssoState.data = [{ provider: "google", isDisabled: false }];
    render(<SSOProviderList />);
    expect(
      screen.getAllByTestId("sso-card").some((c) => c.textContent === "google"),
    ).toBe(true);
  });
});
