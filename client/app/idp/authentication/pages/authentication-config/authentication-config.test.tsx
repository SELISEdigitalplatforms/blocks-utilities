import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { NuqsTestingAdapter } from "nuqs/adapters/testing";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { AuthenticationConfig } from "./authentication-config";

// The tab panels and toolbar actions pull in heavy feature trees; stub them so
// this test focuses on the config page's own tab wiring.
vi.mock("./sso", () => ({ SSO: () => <div data-testid="sso" /> }));
vi.mock("./general/certificates/certificates", () => ({
  Certificates: () => <div data-testid="certificates" />,
}));
vi.mock("@blocks-idp/authentication/components/oidc", () => ({
  OIDC: () => <div data-testid="oidc" />,
}));
vi.mock("@blocks-idp/authentication/components/client-credentials", () => ({
  ClientCredentials: () => <div data-testid="client-credentials" />,
}));
vi.mock("@blocks-idp/authentication/components/create-client-credential", () => ({
  CreateClientCredential: () => <div data-testid="create-client-credential" />,
}));
vi.mock("@blocks-idp/authentication/components/create-oidc", () => ({
  CreateOIDC: () => <div data-testid="create-oidc" />,
}));

const renderConfig = (search = "") => {
  useProjectStore.setState({ selectedProject: { tenantId: "tg1" } as never });
  return render(
    <NuqsTestingAdapter searchParams={search}>
      <AuthenticationConfig />
    </NuqsTestingAdapter>,
  );
};

describe("AuthenticationConfig", () => {
  it("defaults to the social tab and shows the SSO panel", () => {
    renderConfig();
    expect(screen.getByText("IDP")).toBeInTheDocument();
    expect(screen.getByTestId("sso")).toBeInTheDocument();
    // The tab list renders a trigger per configured tab.
    expect(screen.getByRole("tab", { name: "Users" })).toBeInTheDocument();
  });

  it("shows the create client-credential action on the client-credential tab", () => {
    renderConfig("?tab=client_credential");
    expect(screen.getByTestId("create-client-credential")).toBeInTheDocument();
  });

  it("shows the create OIDC action on the authorization-code tab", () => {
    renderConfig("?tab=authorization_code");
    expect(screen.getByTestId("create-oidc")).toBeInTheDocument();
  });
});
