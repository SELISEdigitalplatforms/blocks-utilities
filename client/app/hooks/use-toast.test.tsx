import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import {
  reducer,
  useToast,
  toast,
  showSuccessToast,
  showInfoToast,
  showErrorToast,
} from "./use-toast";

const baseToast = { id: "1", open: true };

afterEach(() => {
  // Clear any toasts left in the shared memory state between tests.
  const { result } = renderHook(() => useToast());
  act(() => result.current.dismiss());
});

describe("reducer", () => {
  it("ADD_TOAST prepends and respects the limit of one", () => {
    let state = reducer({ toasts: [] }, { type: "ADD_TOAST", toast: baseToast });
    expect(state.toasts).toHaveLength(1);
    state = reducer(state, {
      type: "ADD_TOAST",
      toast: { id: "2", open: true },
    });
    expect(state.toasts).toHaveLength(1);
    expect(state.toasts[0].id).toBe("2");
  });

  it("UPDATE_TOAST merges matching toast props", () => {
    const state = reducer(
      { toasts: [{ ...baseToast, title: "a" }] },
      { type: "UPDATE_TOAST", toast: { id: "1", title: "b" } },
    );
    expect(state.toasts[0].title).toBe("b");
  });

  it("DISMISS_TOAST closes a specific toast", () => {
    const state = reducer(
      { toasts: [{ ...baseToast }] },
      { type: "DISMISS_TOAST", toastId: "1" },
    );
    expect(state.toasts[0].open).toBe(false);
  });

  it("DISMISS_TOAST with no id closes all toasts", () => {
    const state = reducer(
      { toasts: [{ ...baseToast }, { id: "2", open: true }] },
      { type: "DISMISS_TOAST" },
    );
    expect(state.toasts.every((t) => t.open === false)).toBe(true);
  });

  it("REMOVE_TOAST with no id clears everything", () => {
    const state = reducer(
      { toasts: [{ ...baseToast }] },
      { type: "REMOVE_TOAST", toastId: undefined },
    );
    expect(state.toasts).toEqual([]);
  });

  it("REMOVE_TOAST removes a single toast by id", () => {
    const state = reducer(
      { toasts: [{ ...baseToast }, { id: "2", open: true }] },
      { type: "REMOVE_TOAST", toastId: "1" },
    );
    expect(state.toasts.map((t) => t.id)).toEqual(["2"]);
  });
});

describe("toast()", () => {
  it("adds a toast and returns controls", () => {
    const { result } = renderHook(() => useToast());
    let handle: ReturnType<typeof toast> | undefined;
    act(() => {
      handle = toast({ title: "Hello" });
    });
    expect(result.current.toasts[0].title).toBe("Hello");
    expect(handle?.id).toBeDefined();

    act(() => handle?.update({ ...result.current.toasts[0], title: "Updated" }));
    expect(result.current.toasts[0].title).toBe("Updated");

    act(() => handle?.dismiss());
    expect(result.current.toasts[0].open).toBe(false);
  });

  it("dismisses via onOpenChange(false)", () => {
    const { result } = renderHook(() => useToast());
    act(() => {
      toast({ title: "X" });
    });
    act(() => result.current.toasts[0].onOpenChange?.(false));
    expect(result.current.toasts[0].open).toBe(false);
  });
});

describe("toast helpers", () => {
  it("showSuccessToast adds a success variant", () => {
    const { result } = renderHook(() => useToast());
    act(() => showSuccessToast({ description: "done" }));
    expect(result.current.toasts[0]).toMatchObject({
      variant: "success",
      title: "Success",
      description: "done",
    });
  });

  it("showInfoToast adds an info variant", () => {
    const { result } = renderHook(() => useToast());
    act(() => showInfoToast({ description: "fyi" }));
    expect(result.current.toasts[0]).toMatchObject({
      variant: "info",
      title: "Info",
    });
  });

  it("showErrorToast renders a single string message", () => {
    const { result } = renderHook(() => useToast());
    act(() => showErrorToast({ errors: "boom" }));
    expect(result.current.toasts[0]).toMatchObject({
      variant: "destructive",
      title: "Failed",
      description: "boom",
    });
  });

  it("showErrorToast maps an array of messages to nodes", () => {
    const { result } = renderHook(() => useToast());
    act(() =>
      showErrorToast({ errors: { a: "one", b: "two" } }),
    );
    expect(Array.isArray(result.current.toasts[0].description)).toBe(true);
  });
});
