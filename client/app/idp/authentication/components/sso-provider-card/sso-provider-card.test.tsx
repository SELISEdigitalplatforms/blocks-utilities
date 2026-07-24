import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

import { SSOProviderCard, SSOProviderCardSkelton } from "./sso-provider-card";

vi.mock("../sso-provider-status-toggle", () => ({
  SSoProviderStatusToggle: () => <div data-testid="status-toggle" />,
}));

const baseConfig = {
  provider: "google",
  label: "Google",
  description: "Google sign in",
  imageSrc: "/google.png",
  imageSrcDark: "/google-dark.png",
  isAvailable: true,
  isConfigured: true,
  itemId: "cfg-1",
  isDisabled: false,
} as never;

const renderCard = (config: unknown) =>
  render(
    <MemoryRouter>
      <SSOProviderCard configuration={config as never} />
    </MemoryRouter>,
  );

describe("SSOProviderCard", () => {
  it("renders an active configured provider", () => {
    renderCard(baseConfig);
    expect(screen.getByText("Google")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("Google sign in")).toBeInTheDocument();
    expect(screen.getByTestId("status-toggle")).toBeInTheDocument();
  });

  it("shows a coming soon badge and no menu for unavailable providers", () => {
    renderCard({ ...baseConfig, isAvailable: false, itemId: "" });
    expect(screen.getByText("Coming soon")).toBeInTheDocument();
    // No configure menu trigger when the provider is unavailable.
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("renders a menu trigger for an available provider", () => {
    renderCard({ ...baseConfig, isDisabled: true });
    expect(screen.getByRole("button")).toBeInTheDocument();
    // Active badge is hidden while disabled.
    expect(screen.queryByText("Active")).not.toBeInTheDocument();
  });

  it("renders the skeleton placeholder", () => {
    const { container } = render(<SSOProviderCardSkelton />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });
});
