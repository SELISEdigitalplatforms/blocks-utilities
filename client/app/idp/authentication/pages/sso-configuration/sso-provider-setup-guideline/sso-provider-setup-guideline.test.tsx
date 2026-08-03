import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { SSoProviderSetupGuideLine } from "./sso-provider-setup-guideline";
import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";

vi.mock("./sso-setup-guideline", () => ({
  SSOSetupGuideLine: () => <div data-testid="guide-steps" />,
}));
vi.mock("./sso-setup-guideline-steps-docs", () => ({
  SSOSetupGuideSteps: {
    google: [{ id: "1", description: "step" }],
    github: null,
  },
}));

describe("SSoProviderSetupGuideLine", () => {
  it("renders nothing when the provider has no steps", () => {
    const { container } = render(
      <SSoProviderSetupGuideLine provider={SSO_PROVIDERS.github} open onOpenChange={vi.fn()} />,
    );
    expect(container.firstChild).toBeNull();
  });

  it("renders the setup guide panel when open", () => {
    render(
      <SSoProviderSetupGuideLine provider={SSO_PROVIDERS.google} open onOpenChange={vi.fn()} />,
    );
    expect(screen.getByText("Setup Guide")).toBeInTheDocument();
    expect(screen.getByTestId("guide-steps")).toBeInTheDocument();
  });

  it("closes the panel when the close button is clicked", async () => {
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    render(
      <SSoProviderSetupGuideLine
        provider={SSO_PROVIDERS.google}
        open
        onOpenChange={onOpenChange}
      />,
    );
    await user.click(screen.getByRole("button"));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("renders nothing when closed", () => {
    const { container } = render(
      <SSoProviderSetupGuideLine provider={SSO_PROVIDERS.google} open={false} onOpenChange={vi.fn()} />,
    );
    expect(container.querySelector('[data-testid="guide-steps"]')).toBeNull();
  });
});
