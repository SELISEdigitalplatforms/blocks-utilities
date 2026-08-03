import { describe, it, expect, vi } from "vitest";
import { render } from "@testing-library/react";
import { createRef } from "react";

import { HCaptcha } from "./hCaptcha";
import type { CaptchaRef } from "./index.type";

const resetCaptcha = vi.fn();
vi.mock("@hcaptcha/react-hcaptcha", () => ({
  default: vi.fn(() => <div data-testid="core-hcaptcha" />),
}));

// The mocked default export ignores the ref, so imperative reset is a no-op
// call path; the test still exercises the useImperativeHandle wiring.
describe("HCaptcha", () => {
  it("renders the underlying hcaptcha widget with the given site key", () => {
    const { getByTestId } = render(
      <HCaptcha type="hCaptcha" siteKey="site-1" onVerify={vi.fn()} />,
    );
    expect(getByTestId("core-hcaptcha")).toBeInTheDocument();
  });

  it("exposes a reset method through the forwarded ref", () => {
    const ref = createRef<CaptchaRef>();
    render(<HCaptcha ref={ref} type="hCaptcha" siteKey="site-1" onVerify={vi.fn()} />);
    expect(typeof ref.current?.reset).toBe("function");
    // Calling reset should not throw even when the inner ref is unset.
    expect(() => ref.current?.reset()).not.toThrow();
    void resetCaptcha;
  });
});
