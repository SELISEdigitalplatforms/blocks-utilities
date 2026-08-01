import { act, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import usePopoverWidth from "./use-popover-width";

describe("usePopoverWidth", () => {
  it("returns a ref and undefined width before measurement", () => {
    const { result } = renderHook(() => usePopoverWidth());
    const [ref, width] = result.current;
    expect(ref).toHaveProperty("current");
    expect(width).toBeUndefined();
  });

  it("measures the button width from its ref", () => {
    const { result, rerender } = renderHook(() => usePopoverWidth());
    const [ref] = result.current;
    Object.defineProperty(ref, "current", {
      value: { offsetWidth: 240 } as HTMLButtonElement,
      writable: true,
      configurable: true,
    });
    act(() => {
      window.dispatchEvent(new Event("resize"));
    });
    rerender();
    expect(result.current[1]).toBe(240);
  });
});
