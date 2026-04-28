import { renderHook, act } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import useIsServiceBarOpenComm from "./use-is-service-tab-open-comm";

describe("useIsServiceBarOpenComm", () => {
  let originalInnerWidth: number;

  beforeEach(() => {
    // Save original innerWidth
    originalInnerWidth = window.innerWidth;
  });

  afterEach(() => {
    // Restore original innerWidth
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

    const { result } = renderHook(() => useIsServiceBarOpenComm());

    // Window width (1000) <= default breakpoint (1038), so should be true
    expect(result.current).toBe(true);
  });

  it("should return false when window width is greater than breakpoint", () => {
    setWindowWidth(1200);

    const { result } = renderHook(() => useIsServiceBarOpenComm());

    // Window width (1200) > default breakpoint (1038), so should be false
    expect(result.current).toBe(false);
  });

  it("should update state when window is resized below breakpoint", () => {
    setWindowWidth(1200);

    const { result } = renderHook(() => useIsServiceBarOpenComm());

    expect(result.current).toBe(false);

    // Resize to below breakpoint
    setWindowWidth(1000);
    triggerResize();

    expect(result.current).toBe(true);
  });

  it("should update state when window is resized above breakpoint", () => {
    setWindowWidth(1000);

    const { result } = renderHook(() => useIsServiceBarOpenComm());

    expect(result.current).toBe(true);

    // Resize to above breakpoint
    setWindowWidth(1200);
    triggerResize();

    expect(result.current).toBe(false);
  });

  it("should use custom breakpoint when provided", () => {
    setWindowWidth(800);

    const { result } = renderHook(() => useIsServiceBarOpenComm(900));

    // Window width (800) <= custom breakpoint (900), so should be true
    expect(result.current).toBe(true);
  });

  it("should respect custom breakpoint on resize", () => {
    setWindowWidth(950);

    const { result } = renderHook(() => useIsServiceBarOpenComm(900));

    // Window width (950) > custom breakpoint (900), so should be false
    expect(result.current).toBe(false);

    // Resize to below custom breakpoint
    setWindowWidth(850);
    triggerResize();

    expect(result.current).toBe(true);
  });

  it("should handle exact breakpoint value (equal to breakpoint)", () => {
    setWindowWidth(1038);

    const { result } = renderHook(() => useIsServiceBarOpenComm());

    // Window width (1038) <= breakpoint (1038), so should be true
    expect(result.current).toBe(true);
  });

  it("should handle window width one pixel above breakpoint", () => {
    setWindowWidth(1039);

    const { result } = renderHook(() => useIsServiceBarOpenComm());

    // Window width (1039) > breakpoint (1038), so should be false
    expect(result.current).toBe(false);
  });

  it("should handle window width one pixel below breakpoint", () => {
    setWindowWidth(1037);

    const { result } = renderHook(() => useIsServiceBarOpenComm());

    // Window width (1037) <= breakpoint (1038), so should be true
    expect(result.current).toBe(true);
  });

  it("should clean up event listener on unmount", () => {
    const removeEventListenerSpy = vi.spyOn(window, "removeEventListener");

    const { unmount } = renderHook(() => useIsServiceBarOpenComm());

    unmount();

    expect(removeEventListenerSpy).toHaveBeenCalledWith("resize", expect.any(Function));
  });

  it("should add event listener on mount", () => {
    const addEventListenerSpy = vi.spyOn(window, "addEventListener");

    renderHook(() => useIsServiceBarOpenComm());

    expect(addEventListenerSpy).toHaveBeenCalledWith("resize", expect.any(Function));
  });

  it("should handle multiple resize events", () => {
    setWindowWidth(1200);

    const { result } = renderHook(() => useIsServiceBarOpenComm());

    expect(result.current).toBe(false);

    // First resize
    setWindowWidth(1000);
    triggerResize();
    expect(result.current).toBe(true);

    // Second resize
    setWindowWidth(1200);
    triggerResize();
    expect(result.current).toBe(false);

    // Third resize
    setWindowWidth(500);
    triggerResize();
    expect(result.current).toBe(true);
  });

  it("should update when breakpoint changes", () => {
    setWindowWidth(950);

    const { result, rerender } = renderHook(
      ({ breakpoint }) => useIsServiceBarOpenComm(breakpoint),
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

    const { result } = renderHook(() => useIsServiceBarOpenComm());

    // Window width (320) <= breakpoint (1038), so should be true
    expect(result.current).toBe(true);
  });

  it("should handle very large window widths", () => {
    setWindowWidth(3840);

    const { result } = renderHook(() => useIsServiceBarOpenComm());

    // Window width (3840) > breakpoint (1038), so should be false
    expect(result.current).toBe(false);
  });
});
