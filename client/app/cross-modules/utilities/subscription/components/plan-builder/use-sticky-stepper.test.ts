import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { previewStickyTop, useStickyStepper } from "./use-sticky-stepper";

/**
 * jsdom has no layout engine and implements neither IntersectionObserver nor ResizeObserver, so
 * these tests drive both observers directly. They prove the state machine and the arithmetic - not
 * that the page actually sticks, which is unobservable here and is verified in a browser instead.
 */

type IoCallback = (entries: IntersectionObserverEntry[]) => void;
type RoCallback = (entries: ResizeObserverEntry[]) => void;

let ioCallback: IoCallback | undefined;
let ioOptions: IntersectionObserverInit | undefined;
let ioObserved: Element[] = [];
let ioDisconnects = 0;
let roCallback: RoCallback | undefined;
let roDisconnects = 0;

const originalIO = globalThis.IntersectionObserver;
const originalRO = globalThis.ResizeObserver;

beforeEach(() => {
  ioCallback = undefined;
  ioOptions = undefined;
  ioObserved = [];
  ioDisconnects = 0;
  roCallback = undefined;
  roDisconnects = 0;

  globalThis.IntersectionObserver = class {
    constructor(cb: IoCallback, options?: IntersectionObserverInit) {
      ioCallback = cb;
      ioOptions = options;
    }
    observe(node: Element) {
      ioObserved.push(node);
    }
    disconnect() {
      ioDisconnects += 1;
    }
    unobserve() {}
    takeRecords() {
      return [];
    }
  } as unknown as typeof IntersectionObserver;

  globalThis.ResizeObserver = class {
    constructor(cb: RoCallback) {
      roCallback = cb;
    }
    observe() {}
    disconnect() {
      roDisconnects += 1;
    }
    unobserve() {}
  } as unknown as typeof ResizeObserver;
});

afterEach(() => {
  globalThis.IntersectionObserver = originalIO;
  globalThis.ResizeObserver = originalRO;
});

/**
 * A ratio below 1 is not enough on its own - the element is also partly clipped when it sits at
 * the BOTTOM of a short viewport, which is nowhere near stuck. `top` is the element's top edge
 * relative to the (1px-inset) root.
 */
const entry = (intersectionRatio: number, top: number) =>
  ({
    intersectionRatio,
    boundingClientRect: { top } as DOMRectReadOnly,
    rootBounds: { top: 1 } as DOMRectReadOnly,
  }) as IntersectionObserverEntry;

const attach = (height = 0) => {
  const node = document.createElement("div");
  vi.spyOn(node, "getBoundingClientRect").mockReturnValue({
    height,
  } as DOMRect);
  return node;
};

describe("useStickyStepper — stuck detection (H3, H5)", () => {
  it("observes the sticky element itself with a 1px top root inset, not a sentinel", () => {
    const { result } = renderHook(() => useStickyStepper());
    const node = attach();
    act(() => result.current.stepperRef(node));

    // The whole point of this construction: no extra DOM node is introduced, so nothing becomes a
    // spacing participant in the page's space-y-5 rhythm.
    expect(ioObserved).toEqual([node]);
    expect(ioOptions).toMatchObject({
      threshold: [1],
      rootMargin: "-1px 0px 0px 0px",
    });
  });

  it("is not stuck while fully intersecting", () => {
    const { result } = renderHook(() => useStickyStepper());
    act(() => result.current.stepperRef(attach()));
    act(() => ioCallback?.([entry(1, 40)]));

    expect(result.current.isStuck).toBe(false);
  });

  it("becomes stuck once the ratio drops below 1", () => {
    const { result } = renderHook(() => useStickyStepper());
    act(() => result.current.stepperRef(attach()));
    act(() => ioCallback?.([entry(0.98, 0)]));

    expect(result.current.isStuck).toBe(true);
  });

  it("H5: returns to unstuck when the element is fully visible again", () => {
    const { result } = renderHook(() => useStickyStepper());
    act(() => result.current.stepperRef(attach()));
    act(() => ioCallback?.([entry(0, -50)]));
    expect(result.current.isStuck).toBe(true);

    act(() => ioCallback?.([entry(1, 60)]));
    expect(result.current.isStuck).toBe(false);
  });

  it("is NOT stuck when merely clipped by the bottom of a short viewport", () => {
    const { result } = renderHook(() => useStickyStepper());
    act(() => result.current.stepperRef(attach()));
    // Ratio < 1 (partly below the fold) but the top edge is far below the root top.
    act(() => ioCallback?.([entry(0.4, 500)]));

    expect(result.current.isStuck).toBe(false);
  });

  it("starts unstuck before any observation", () => {
    const { result } = renderHook(() => useStickyStepper());
    expect(result.current.isStuck).toBe(false);
  });
});

describe("useStickyStepper — height measurement (H2)", () => {
  it("measures synchronously on attach, so the preview never renders at the wrong offset first", () => {
    const { result } = renderHook(() => useStickyStepper());
    act(() => result.current.stepperRef(attach(132)));

    expect(result.current.stepperHeight).toBe(132);
  });

  it("prefers the border box over the content box", () => {
    const { result } = renderHook(() => useStickyStepper());
    act(() => result.current.stepperRef(attach(100)));

    act(() =>
      roCallback?.([
        {
          borderBoxSize: [{ blockSize: 150, inlineSize: 800 }],
          contentRect: { height: 120 },
          target: document.createElement("div"),
        } as unknown as ResizeObserverEntry,
      ]),
    );

    // 150 (border box), not 120 (content box) - the stuck treatment lives inside the border box.
    expect(result.current.stepperHeight).toBe(150);
  });

  it("falls back to the bounding rect when borderBoxSize is unavailable", () => {
    const { result } = renderHook(() => useStickyStepper());
    act(() => result.current.stepperRef(attach(100)));

    const target = attach(141);
    act(() =>
      roCallback?.([
        { borderBoxSize: undefined, target } as unknown as ResizeObserverEntry,
      ]),
    );

    expect(result.current.stepperHeight).toBe(141);
  });

  it("tracks a later resize, e.g. crossing a breakpoint", () => {
    const { result } = renderHook(() => useStickyStepper());
    act(() => result.current.stepperRef(attach(140)));

    act(() =>
      roCallback?.([
        {
          borderBoxSize: [{ blockSize: 96, inlineSize: 400 }],
          target: document.createElement("div"),
        } as unknown as ResizeObserverEntry,
      ]),
    );

    expect(result.current.stepperHeight).toBe(96);
  });

  it("disconnects both observers on unmount", () => {
    const { result, unmount } = renderHook(() => useStickyStepper());
    act(() => result.current.stepperRef(attach()));
    unmount();

    expect(ioDisconnects).toBe(1);
    expect(roDisconnects).toBe(1);
  });
});

describe("useStickyStepper — environments without the observers", () => {
  it("renders and reports sane defaults when neither observer exists", () => {
    // @ts-expect-error deliberately removing the globals
    delete globalThis.IntersectionObserver;
    // @ts-expect-error deliberately removing the globals
    delete globalThis.ResizeObserver;

    const { result } = renderHook(() => useStickyStepper());
    expect(() => act(() => result.current.stepperRef(attach(120)))).not.toThrow();

    // Degrades to "never stuck", but the synchronous measurement still works.
    expect(result.current.isStuck).toBe(false);
    expect(result.current.stepperHeight).toBe(120);
  });
});

describe("previewStickyTop (H2 arithmetic)", () => {
  it("clears the stepper and keeps one space-y-5 rhythm unit of gap", () => {
    expect(previewStickyTop(132)).toBe("calc(132px + 1.25rem)");
  });

  it("uses rem for the rhythm rather than assuming top-5 is exactly 20px", () => {
    expect(previewStickyTop(0)).toContain("1.25rem");
  });

  it("rounds sub-pixel measurements UP, so a fraction can never overlap", () => {
    expect(previewStickyTop(131.6)).toBe("calc(132px + 1.25rem)");
    // Ceil, not round: rounding 131.2 down to 131 would leave a sliver of overlap.
    expect(previewStickyTop(131.2)).toBe("calc(132px + 1.25rem)");
  });

  it("never produces a negative offset", () => {
    expect(previewStickyTop(-40)).toBe("calc(0px + 1.25rem)");
  });
});
