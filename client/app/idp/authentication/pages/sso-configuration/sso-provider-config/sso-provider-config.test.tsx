import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";

import { SSOProviderConfig } from "./sso-provider-config";
import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";

vi.mock("./sso-provider-config-forms/sso-provider-config-forms", () => ({
  SsoProviderConfigForms: () => <div data-testid="config-forms" />,
}));
vi.mock("../sso-provider-setup-guideline", () => ({
  SSoProviderSetupGuideLine: ({ open }: { open: boolean }) => (
    <div data-testid="setup-guide">{open ? "open" : "closed"}</div>
  ),
}));
vi.mock("@/components/breadcrumb/breadcrumb", () => ({
  default: () => <div data-testid="breadcrumb" />,
}));

const renderConfig = (provider: SSO_PROVIDERS | "") =>
  render(
    <MemoryRouter>
      <SSOProviderConfig provider={provider as SSO_PROVIDERS} id="cfg-1" />
    </MemoryRouter>,
  );

describe("SSOProviderConfig", () => {
  it("returns null when no provider is given", () => {
    const { container } = renderConfig("");
    expect(container.firstChild).toBeNull();
  });

  it("renders the header, forms and setup guide for a provider", () => {
    renderConfig(SSO_PROVIDERS.google);
    expect(screen.getByText("GOOGLE")).toBeInTheDocument();
    expect(screen.getByTestId("config-forms")).toBeInTheDocument();
    expect(screen.getByTestId("setup-guide")).toHaveTextContent("closed");
  });

  it("toggles the setup guide open when the button is clicked", async () => {
    const user = userEvent.setup();
    renderConfig(SSO_PROVIDERS.google);
    await user.click(screen.getByRole("button", { name: /Setup Guide/i }));
    expect(screen.getByTestId("setup-guide")).toHaveTextContent("open");
  });
});
