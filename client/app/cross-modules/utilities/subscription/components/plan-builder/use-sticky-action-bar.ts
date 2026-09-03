import { useCallback, useEffect, useState } from "react";

/**
 * Whether the builder's action bar is currently pinned to the bottom of the viewport.
 *
 * The mirror image of {@link useStickyStepper}'s top-edge detection, and it exists for the same
 * reason: CSS has no `:stuck`, so the sticky element observes itself through an
 * IntersectionObserver whose root is shrunk by 1px - this time at the BOTTOM. The bar can only be
 * fully intersecting while it sits above that line, so the instant it pins to `bottom: 0` it is
 * clipped and the ratio drops below 1.
 *
 * Both conditions in the predicate matter, again for the reasons the stepper documents. The ratio
 * alone is also below 1 when the bar is merely clipped by the TOP of a short viewport or is wider
 * than it, neither of which is stuck. Requiring the bar's bottom edge to be at or below the
 * (1px-inset) root bottom removes those false positives.
 *
 * Kept separate from `useStickyStepper` rather than folded into it behind an `edge` argument: the
 * two share only the observer boilerplate, while the predicate, the root margin and the return
 * shape all differ - the stepper additionally measures its own height to position the preview
 * column, which a bar pinned to the bottom has no equivalent of.
 *
 * Degrades rather than throwing where IntersectionObserver is absent (jsdom): the bar simply never
 * reports stuck, which costs the raised treatment and nothing else - it is still sticky, because
 * that part is pure CSS.
 */
export interface StickyActionBarLayout {
  /** Attach to the element that carries `position: sticky`. */
  barRef: (node: HTMLElement | null) => void;
  /** True while the bar is pinned to the bottom of the viewport with content still below it. */
  isStuck: boolean;
}

export const useStickyActionBar = (): StickyActionBarLayout => {
  const [isStuck, setIsStuck] = useState(false);
  // In state rather than a ref for the reason the stepper spells out: the effect has to re-run
  // when the node attaches, and a ref plus `[]` deps would observe whatever it held at mount.
  const [node, setNode] = useState<HTMLElement | null>(null);

  // Stable identity on purpose - a changing callback ref is detached and reattached every render.
  const barRef = useCallback((next: HTMLElement | null) => setNode(next), []);

  useEffect(() => {
    if (!node || typeof IntersectionObserver === "undefined") return;

    const observer = new IntersectionObserver(
      (entries) => {
        const entry = entries[entries.length - 1];
        if (!entry) return;
        const rootBottom = entry.rootBounds?.bottom ?? 0;
        setIsStuck(
          entry.intersectionRatio < 1 && entry.boundingClientRect.bottom >= rootBottom,
        );
      },
      { threshold: [1], rootMargin: "0px 0px -1px 0px" },
    );

    observer.observe(node);
    return () => observer.disconnect();
  }, [node]);

  return { barRef, isStuck };
};
