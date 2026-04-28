import { renderHook, act } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import useIsServiceBarOpenLocal from "./use-is-service-tab-open-local";

describe("useIsServiceBarOpenLocal", () => {
  let originalInnerWidth: number;

  beforeEach(() => {
    originalInnerWidth = window.innerWidth;
  });

  afterEach(() => {
    Object.defineProperty(window, "innerWidth", {
      writable: true,
      configurable: true,
      value: originalInnerWidth,
    });
    vi.clearAllMocks();
  });

  const setWindowWidth = (width: number) => {
    Object.defineProperty(window, "innerWidth", {
      writable: true,
      configurable: true,
      value: width,
    });
  };

  const triggerResize = () => {
    act(() => {
      window.dispatchEvent(new Event("resize"));
    });
  };

  it("should initialize with correct state based on window width", () => {
    setWindowWidth(1000);

    const { result } = renderHook(() => useIsServiceBarOpenLocal());

    // Window width (1000) <= default breakpoint (1134), so should be true
    expect(result.current).toBe(true);
  });

  it("should return false when window width is greater than breakpoint", () => {
    setWindowWidth(1200);

    const { result } = renderHook(() => useIsServiceBarOpenLocal());

    // Window width (1200) > default breakpoint (1134), so should be false
    expect(result.current).toBe(false);
  });

  it("should update state when window is resized below breakpoint", () => {
    setWindowWidth(1200);

    const { result } = renderHook(() => useIsServiceBarOpenLocal());

    expect(result.current).toBe(false);

    setWindowWidth(1000);
    triggerResize();

    expect(result.current).toBe(true);
  });

  it("should update state when window is resized above breakpoint", () => {
    setWindowWidth(1000);

    const { result } = renderHook(() => useIsServiceBarOpenLocal());

    expect(result.current).toBe(true);

    setWindowWidth(1200);
    triggerResize();

    expect(result.current).toBe(false);
  });

  it("should use custom breakpoint when provided", () => {
    setWindowWidth(800);

    const { result } = renderHook(() => useIsServiceBarOpenLocal(900));

    // Window width (800) <= custom breakpoint (900), so should be true
    expect(result.current).toBe(true);
  });

  it("should return false when width exceeds custom breakpoint", () => {
    setWindowWidth(950);

    const { result } = renderHook(() => useIsServiceBarOpenLocal(900));

    // Window width (950) > custom breakpoint (900), so should be false
    expect(result.current).toBe(false);
  });

  it("should handle exact default breakpoint value (1134)", () => {
    setWindowWidth(1134);

    const { result } = renderHook(() => useIsServiceBarOpenLocal());

    // Window width (1134) === breakpoint (1134), so should be true (<=)
    expect(result.current).toBe(true);
  });

  it("should return false at one pixel above breakpoint", () => {
    setWindowWidth(1135);

    const { result } = renderHook(() => useIsServiceBarOpenLocal());

    // Window width (1135) > breakpoint (1134), so should be false
    expect(result.current).toBe(false);
  });

  it("should return true at one pixel below breakpoint", () => {
    setWindowWidth(1133);

    const { result } = renderHook(() => useIsServiceBarOpenLocal());

    // Window width (1133) <= breakpoint (1134), so should be true
    expect(result.current).toBe(true);
  });

  it("should clean up event listener on unmount", () => {
    const removeEventListenerSpy = vi.spyOn(window, "removeEventListener");

    const { unmount } = renderHook(() => useIsServiceBarOpenLocal());

    unmount();

    expect(removeEventListenerSpy).toHaveBeenCalledWith("resize", expect.any(Function));
  });

  it("should add event listener on mount", () => {
    const addEventListenerSpy = vi.spyOn(window, "addEventListener");

    renderHook(() => useIsServiceBarOpenLocal());

    expect(addEventListenerSpy).toHaveBeenCalledWith("resize", expect.any(Function));
  });

  it("should handle multiple resize events", () => {
    setWindowWidth(1200);

    const { result } = renderHook(() => useIsServiceBarOpenLocal());

    expect(result.current).toBe(false);

    setWindowWidth(1000);
    triggerResize();
    expect(result.current).toBe(true);

    setWindowWidth(1200);
    triggerResize();
    expect(result.current).toBe(false);

    setWindowWidth(500);
    triggerResize();
    expect(result.current).toBe(true);
  });

  it("should update when breakpoint prop changes", () => {
    setWindowWidth(950);

    const { result, rerender } = renderHook(
      ({ breakpoint }) => useIsServiceBarOpenLocal(breakpoint),
      { initialProps: { breakpoint: 900 } },
    );

    // Window width (950) > breakpoint (900), so should be false
    expect(result.current).toBe(false);

    // Change breakpoint to 1000
    rerender({ breakpoint: 1000 });

    // Now window width (950) <= breakpoint (1000), so should be true
    expect(result.current).toBe(true);
  });

  it("should handle very small window widths", () => {
    setWindowWidth(320);

    const { result } = renderHook(() => useIsServiceBarOpenLocal());

    // Window width (320) <= breakpoint (1134), so should be true
    expect(result.current).toBe(true);
  });

  it("should handle very large window widths", () => {
    setWindowWidth(3840);

    const { result } = renderHook(() => useIsServiceBarOpenLocal());

    // Window width (3840) > breakpoint (1134), so should be false
    expect(result.current).toBe(false);
  });
});
