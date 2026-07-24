import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useCopyToClipboard } from "./use-copy-to-clipboard";

describe("useCopyToClipboard", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  const setClipboard = (impl?: (text: string) => Promise<void>) => {
    Object.defineProperty(navigator, "clipboard", {
      value: impl ? { writeText: vi.fn(impl) } : undefined,
      writable: true,
      configurable: true,
    });
  };

  it("copies text and invokes onSuccess", async () => {
    setClipboard(() => Promise.resolve());
    const onSuccess = vi.fn();
    const { result } = renderHook(() => useCopyToClipboard());

    await act(async () => {
      await result.current.copy("hello", onSuccess);
    });

    expect(navigator.clipboard.writeText).toHaveBeenCalledWith("hello");
    expect(onSuccess).toHaveBeenCalled();
  });

  it("calls onError when the clipboard API is missing", async () => {
    setClipboard(undefined);
    const onError = vi.fn();
    const { result } = renderHook(() => useCopyToClipboard());

    await act(async () => {
      await result.current.copy("x", undefined, onError);
    });

    expect(onError).toHaveBeenCalledWith(expect.any(Error));
  });

  it("calls onError when writeText rejects", async () => {
    setClipboard(() => Promise.reject(new Error("denied")));
    const onError = vi.fn();
    const { result } = renderHook(() => useCopyToClipboard());

    await act(async () => {
      await result.current.copy("x", undefined, onError);
    });

    expect(onError).toHaveBeenCalledWith(expect.objectContaining({ message: "denied" }));
  });

  it("resolves without throwing when no callbacks are supplied", async () => {
    setClipboard(() => Promise.resolve());
    const { result } = renderHook(() => useCopyToClipboard());
    await act(async () => {
      await result.current.copy("x");
    });
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith("x");
  });
});
