import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import useIsMobile from "./use-is-mobile";

const setWidth = (w: number) => {
  Object.defineProperty(window, "innerWidth", {
    value: w,
    writable: true,
    configurable: true,
  });
};

describe("useIsMobile", () => {
  afterEach(() => setWidth(1024));

  it("returns true when width is at or below the breakpoint", () => {
    setWidth(500);
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(true);
  });

  it("returns false above the breakpoint", () => {
    setWidth(1200);
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(false);
  });

  it("responds to resize events", () => {
    setWidth(1200);
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(false);
    act(() => {
      setWidth(400);
      window.dispatchEvent(new Event("resize"));
    });
    expect(result.current).toBe(true);
  });

  it("honors a custom breakpoint", () => {
    setWidth(600);
    const { result } = renderHook(() => useIsMobile(500));
    expect(result.current).toBe(false);
  });
});
