import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useCountDown } from "./use-count-down";

describe("useCountDown", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("decrements every second", () => {
    const { result } = renderHook(() => useCountDown(3));
    expect(result.current.remainingTime).toBe(3);
    act(() => vi.advanceTimersByTime(2000));
    expect(result.current.remainingTime).toBe(1);
  });

  it("stops at zero and does not go negative", () => {
    const { result } = renderHook(() => useCountDown(1));
    // Advance one tick at a time so the effect can clear the interval once it
    // reaches zero (a single large jump would fire the pending interval
    // callbacks before React re-runs the guarded effect).
    act(() => vi.advanceTimersByTime(1000));
    expect(result.current.remainingTime).toBe(0);
    act(() => vi.advanceTimersByTime(3000));
    expect(result.current.remainingTime).toBe(0);
  });

  it("reset restores the initial value", () => {
    const { result } = renderHook(() => useCountDown(5));
    act(() => vi.advanceTimersByTime(3000));
    act(() => result.current.reset());
    expect(result.current.remainingTime).toBe(5);
  });

  it("reset accepts an explicit value", () => {
    const { result } = renderHook(() => useCountDown(5));
    act(() => result.current.reset(10));
    expect(result.current.remainingTime).toBe(10);
  });
});
