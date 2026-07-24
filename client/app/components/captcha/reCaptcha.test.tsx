import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render } from "@testing-library/react";
import { createRef } from "react";
import { ReCaptcha } from "./reCaptcha";
import type { CaptchaRef } from "./index.type";

describe("ReCaptcha", () => {
  const render_ = vi.fn(() => 7);
  const ready = vi.fn((cb: () => void) => cb());
  const reset = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    document.getElementById("blocks-recaptcha-script")?.remove();
  });
  afterEach(() => {
    delete (window as { grecaptcha?: unknown }).grecaptcha;
  });

  it("renders the widget immediately when grecaptcha is ready", () => {
    (window as { grecaptcha?: unknown }).grecaptcha = { render: render_, ready, reset };
    const onVerify = vi.fn();
    render(<ReCaptcha siteKey="key-1" onVerify={onVerify} />);
    expect(ready).toHaveBeenCalled();
    expect(render_).toHaveBeenCalledWith(
      expect.any(HTMLElement),
      expect.objectContaining({ sitekey: "key-1", theme: "light", size: "normal" }),
    );
  });

  it("passes expired and error callbacks through", () => {
    (window as { grecaptcha?: unknown }).grecaptcha = { render: render_, ready, reset };
    render(
      <ReCaptcha
        siteKey="key-2"
        onVerify={vi.fn()}
        onExpired={vi.fn()}
        onError={vi.fn()}
        theme="dark"
        size="compact"
      />,
    );
    expect(render_).toHaveBeenCalledWith(
      expect.any(HTMLElement),
      expect.objectContaining({
        theme: "dark",
        size: "compact",
        "expired-callback": expect.any(Function),
        "error-callback": expect.any(Function),
      }),
    );
  });

  it("exposes a reset method through the ref", () => {
    (window as { grecaptcha?: unknown }).grecaptcha = { render: render_, ready, reset };
    const ref = createRef<CaptchaRef>();
    render(<ReCaptcha ref={ref} siteKey="key-3" onVerify={vi.fn()} />);
    ref.current?.reset();
    expect(reset).toHaveBeenCalledWith(7);
  });

  it("injects the loader script when grecaptcha is not available", () => {
    render(<ReCaptcha siteKey="key-4" onVerify={vi.fn()} />);
    const script = document.getElementById("blocks-recaptcha-script");
    expect(script).toBeTruthy();
    expect(script?.getAttribute("src")).toContain("recaptcha/api.js");
  });
});
