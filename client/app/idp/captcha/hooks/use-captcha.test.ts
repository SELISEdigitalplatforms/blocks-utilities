import { renderHook, act } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useTheme } from "@/hooks/use-theme";

vi.mock("@/hooks/use-theme", () => ({
  useTheme: vi.fn(() => ({ theme: "light" })),
}));

import { useCaptcha } from "./use-captcha";

describe("useCaptcha", () => {
  it("should return initial empty code", () => {
    const { result } = renderHook(() =>
      useCaptcha({ siteKey: "test-site-key", type: "reCaptcha-v2-checkbox" }),
    );

    expect(result.current.code).toBe("");
  });

  it("should return captcha config with correct props", () => {
    const { result } = renderHook(() =>
      useCaptcha({ siteKey: "test-site-key", type: "reCaptcha-v2-checkbox" }),
    );

    expect(result.current.captcha.siteKey).toBe("test-site-key");
    expect(result.current.captcha.type).toBe("reCaptcha-v2-checkbox");
    expect(result.current.captcha.theme).toBe("light");
  });

  it("should set dark theme when theme is dark", () => {
    vi.mocked(useTheme).mockReturnValue({ theme: "dark", setTheme: vi.fn(), themes: [] });

    const { result } = renderHook(() =>
      useCaptcha({ siteKey: "test-site-key", type: "reCaptcha-v2-checkbox" }),
    );

    expect(result.current.captcha.theme).toBe("dark");
  });

  it("should update code on verify", () => {
    const { result } = renderHook(() =>
      useCaptcha({ siteKey: "test-site-key", type: "reCaptcha-v2-checkbox" }),
    );

    act(() => {
      result.current.captcha.onVerify("captcha-token-123");
    });

    expect(result.current.code).toBe("captcha-token-123");
  });

  it("should clear code on expired", () => {
    const { result } = renderHook(() =>
      useCaptcha({ siteKey: "test-site-key", type: "reCaptcha-v2-checkbox" }),
    );

    act(() => {
      result.current.captcha.onVerify("captcha-token-123");
    });
    expect(result.current.code).toBe("captcha-token-123");

    act(() => {
      result.current.captcha.onExpired();
    });
    expect(result.current.code).toBe("");
  });

  it("should clear code on error", () => {
    const { result } = renderHook(() =>
      useCaptcha({ siteKey: "test-site-key", type: "reCaptcha-v2-checkbox" }),
    );

    act(() => {
      result.current.captcha.onVerify("captcha-token-123");
    });

    act(() => {
      result.current.captcha.onError();
    });
    expect(result.current.code).toBe("");
  });

  it("should provide a ref", () => {
    const { result } = renderHook(() =>
      useCaptcha({ siteKey: "test-site-key", type: "reCaptcha-v2-checkbox" }),
    );

    expect(result.current.ref).toBeDefined();
    expect(result.current.captcha.ref).toBe(result.current.ref);
  });
});
