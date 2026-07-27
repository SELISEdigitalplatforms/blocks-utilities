import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";

import { Captcha } from "./captcha";

vi.mock("./reCaptcha", () => ({
  ReCaptcha: () => <div data-testid="recaptcha" />,
}));
vi.mock("./hCaptcha", () => ({
  HCaptcha: () => <div data-testid="hcaptcha" />,
}));

describe("Captcha", () => {
  it("renders the reCaptcha implementation", () => {
    render(<Captcha type="reCaptcha-v2-checkbox" siteKey="k" onVerify={vi.fn()} />);
    expect(screen.getByTestId("recaptcha")).toBeInTheDocument();
  });

  it("renders the hCaptcha implementation", () => {
    render(<Captcha type="hCaptcha" siteKey="k" onVerify={vi.fn()} />);
    expect(screen.getByTestId("hcaptcha")).toBeInTheDocument();
  });

  it("throws when no type is provided", () => {
    expect(() => render(<Captcha type={undefined as never} siteKey="k" onVerify={vi.fn()} />)).toThrow(
      /type is not passed/,
    );
  });

  it("throws for an unsupported type", () => {
    expect(() =>
      render(<Captcha type={"unknown" as never} siteKey="k" onVerify={vi.fn()} />),
    ).toThrow(/not supported/);
  });
});
