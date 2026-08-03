import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";

import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { ConfigureCaptchaList } from "./configure-captcha-list";

vi.mock("../../modals/configure-captcha-modal/", () => ({
  // The real modal wraps its trigger children in a Dialog root; mirror that so
  // the DialogTrigger passed as a child has the context it needs.
  ConfigureCaptchaModal: ({ children }: { children: React.ReactNode }) => (
    <Dialog>{children}</Dialog>
  ),
}));
vi.mock("@blocks-idp/captcha/modals/toggle-captcha-status-modal", () => ({
  ToggleCaptchaStatusModal: () => <div data-testid="toggle-status" />,
}));

const config = {
  itemId: "c1",
  provider: "recaptcha" as const,
  isEnable: true,
  captchaKey: "site-key-value",
  captchaSecret: "secret-key-value",
};

describe("ConfigureCaptchaList", () => {
  it("shows loading skeletons while fetching", () => {
    const { container } = render(<ConfigureCaptchaList isLoading configurations={[]} />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("shows the empty state when there are no configurations", () => {
    render(<ConfigureCaptchaList isLoading={false} configurations={[]} />);
    expect(
      screen.getByText("No configurations found. Please create a new configuration."),
    ).toBeInTheDocument();
  });

  it("renders a card for a known provider configuration", () => {
    render(<ConfigureCaptchaList isLoading={false} configurations={[config as never]} />);
    expect(screen.getByText("Google reCAPTCHA")).toBeInTheDocument();
    expect(screen.getByText("Enable")).toBeInTheDocument();
    expect(screen.getByText("Site Key")).toBeInTheDocument();
    expect(screen.getByText("Secret Key")).toBeInTheDocument();
  });
});
