import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { Certificates } from "./certificates";

let certState: { isLoading: boolean; data: unknown };
let jwtState: { data: unknown; isLoading: boolean };
vi.mock("@blocks-idp/authentication/hooks/use-identifier", () => ({
  useGetSavedPublicCertificates: () => certState,
}));
vi.mock("@blocks-idp/authentication/hooks/use-jwt-claim", () => ({
  useGetJwtClaim: () => jwtState,
}));

vi.mock("./empty-configuration", () => ({
  EmptyConfiguration: () => <div data-testid="empty-config" />,
}));
vi.mock("./add-edit-provider-modal", () => ({
  AddEditProviderModal: () => <div data-testid="add-edit-provider" />,
}));
vi.mock("./map-jwt-claim-modal", () => ({
  default: () => <div data-testid="map-jwt-modal" />,
}));

describe("Certificates", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    certState = { isLoading: false, data: undefined };
    jwtState = { data: undefined, isLoading: false };
    useProjectStore.setState({ selectedProject: { tenantId: "tg-1" } });
  });

  it("shows the loading skeleton while certificates load", () => {
    certState.isLoading = true;
    const { container } = render(<Certificates />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(
      0,
    );
  });

  it("shows the empty configuration when nothing is configured", () => {
    certState.data = { isConfigured: false };
    render(<Certificates />);
    expect(screen.getByTestId("empty-config")).toBeInTheDocument();
  });

  it("renders the connected provider details when configured", () => {
    certState.data = {
      isConfigured: true,
      providerName: "Okta",
      jwksUrl: "https://okta.test/jwks",
      issuer: "https://okta.test",
      audiences: ["aud-1", "aud-2"],
    };
    render(<Certificates />);
    expect(screen.getByText("External IdP")).toBeInTheDocument();
    expect(screen.getByText("Okta")).toBeInTheDocument();
    expect(screen.getByText("https://okta.test/jwks")).toBeInTheDocument();
    expect(screen.getByText("aud-1, aud-2")).toBeInTheDocument();
  });

  it("warns when jwt claims are not mapped", () => {
    certState.data = { isConfigured: true, providerName: "Others" };
    jwtState = { data: undefined, isLoading: false };
    render(<Certificates />);
    expect(screen.getByText(/didn.t map the jwt claims/i)).toBeInTheDocument();
  });
});
