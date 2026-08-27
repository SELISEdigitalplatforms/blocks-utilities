import { useCallback, useEffect, useState } from "react";

/**
 * Layout wiring for the plan builder's sticky stepper and the preview panel that parks beneath it.
 *
 * Two things the DOM cannot tell us declaratively, so they are measured:
 *
 * 1. **Is the stepper currently stuck?** CSS has no `:stuck`, so the sticky element observes
 *    itself through an IntersectionObserver whose root is shrunk by 1px at the top. It can only be
 *    fully intersecting while it sits below that line, so the instant it pins to `top: 0` it is
 *    clipped and the ratio drops below 1.
 *
 *    This deliberately avoids the more obvious sentinel-above-the-element approach, which has two
 *    real defects: a sentinel is equally non-intersecting when it is *below* the viewport (so a
 *    short viewport or a restored scroll position reads as stuck when it is not), and a sentinel
 *    added to the `space-y-5` container becomes a spacing participant, shifting the very threshold
 *    it exists to detect.
 *
 *    Known limit: if the stepper were ever taller than the viewport the ratio could never reach 1
 *    and it would read as permanently stuck. It is a fixed-content bar, so that cannot happen here.
 *
 * 2. **How tall is the stepper?** The preview panel's sticky offset has to clear the stepper's
 *    bottom edge, and the stepper's height genuinely varies - its description line and step titles
 *    are `sm:`-only and the current-step title is `sm:hidden`. A hard-coded offset would overlap at
 *    some widths and gap at others. The border box is measured, because the stuck treatment sits
 *    inside it.
 *
 * Both observers are optional: jsdom implements neither, and a missing one degrades rather than
 * throwing. Precisely: without IntersectionObserver the stepper simply never reports stuck;
 * without ResizeObserver the initial synchronous measurement still happens (it is taken in the
 * callback ref, not the observer) and only later size changes go unnoticed.
 */

export interface StickyStepperLayout {
  /** Attach to the element that carries `position: sticky`. */
  stepperRef: (node: HTMLElement | null) => void;
  /** True while the stepper is pinned to the top of the viewport. */
  isStuck: boolean;
  /** Measured border-box height of the sticky stepper, in px. 0 until first measurement. */
  stepperHeight: number;
}

export const useStickyStepper = (): StickyStepperLayout => {
  const [isStuck, setIsStuck] = useState(false);
  const [stepperHeight, setStepperHeight] = useState(0);
  // The node lives in state, not a ref, so the effect below re-runs when it attaches or is
  // replaced. With a ref plus `[]` deps the observers would be wired against whatever the ref
  // happened to hold at mount - i.e. nothing.
  const [node, setNode] = useState<HTMLElement | null>(null);

  const stepperRef = useCallback((next: HTMLElement | null) => {
    setNode(next);
    // Measure synchronously on attach so the preview never renders one frame at the wrong offset.
    if (next) setStepperHeight(next.getBoundingClientRect().height);
    // Stable identity on purpose: a callback ref that changes between renders is detached and
    // reattached each time, which here would re-enter this setter in a loop.
  }, []);

  useEffect(() => {
    if (!node) return;

    let stickyObserver: IntersectionObserver | undefined;
    let sizeObserver: ResizeObserver | undefined;

    if (typeof IntersectionObserver !== "undefined") {
      stickyObserver = new IntersectionObserver(
        (entries) => {
          const entry = entries[entries.length - 1];
          if (!entry) return;
          // Both conditions matter. The ratio alone is also < 1 when the element is merely
          // clipped by the BOTTOM of a short viewport, or is wider than the viewport, or is
          // taller than it under heavy zoom - all of which would read as "stuck" while the
          // element is nowhere near the top. Requiring its top edge to be at or above the
          // (1px-inset) root top removes those false positives.
          const rootTop = entry.rootBounds?.top ?? 0;
          setIsStuck(
            entry.intersectionRatio < 1 && entry.boundingClientRect.top <= rootTop,
          );
        },
        { threshold: [1], rootMargin: "-1px 0px 0px 0px" },
      );
      stickyObserver.observe(node);
    }

    if (typeof ResizeObserver !== "undefined") {
      sizeObserver = new ResizeObserver((entries) => {
        const entry = entries[entries.length - 1];
        if (!entry) return;
        const borderBox = Array.isArray(entry.borderBoxSize)
          ? entry.borderBoxSize[0]
          : entry.borderBoxSize;
        const height =
          borderBox?.blockSize ?? entry.target.getBoundingClientRect().height;
        setStepperHeight(height);
      });
      sizeObserver.observe(node);
    }

    return () => {
      stickyObserver?.disconnect();
      sizeObserver?.disconnect();
    };
  }, [node]);

  return { stepperRef, isStuck, stepperHeight };
};

/**
 * Sticky offset for the preview panel: clear the stepper, then keep the page's existing
 * `space-y-5` rhythm. Expressed in rem rather than assuming `top-5` is always 20px.
 */
export const previewStickyTop = (stepperHeight: number): string =>
  // Ceil, not round: rounding a fractional height DOWN (browser zoom, fractional layout) would
  // leave up to half a pixel of overlap, and H2 forbids overlap outright. Erring upward only ever
  // costs a sub-pixel of extra gap.
  `calc(${Math.max(0, Math.ceil(stepperHeight))}px + 1.25rem)`;
