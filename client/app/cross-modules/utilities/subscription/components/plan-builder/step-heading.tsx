import type { ReactNode } from "react";

/**
 * The title block every step opens with.
 *
 * Shared rather than repeated per step so the five of them cannot drift apart on type scale or
 * spacing - which is exactly what had happened, each step carrying its own `text-lg font-semibold`
 * heading and its own margin.
 *
 * The eyebrow is what makes a step recognisable at a glance when the sticky stepper is collapsed
 * to numbers on a narrow screen, so it repeats the step's own name rather than a generic label.
 */
export const StepHeading = ({
  eyebrow,
  title,
  description,
}: {
  eyebrow: string;
  title: string;
  description: ReactNode;
}) => (
  <div className="relative">
    {/*
      A hairline that fades out, standing in for the heavy full-width rule this used to have no
      room for. Decorative only.
    */}
    <span
      aria-hidden="true"
      className="absolute -left-5 top-1 hidden h-[calc(100%-0.25rem)] w-px bg-gradient-to-b from-blocks-secondary-500 via-blocks-primary-400 to-transparent sm:-left-7 sm:block"
    />
    <span className="inline-flex items-center gap-1.5 rounded-full border border-blocks-secondary-200/70 bg-blocks-secondary-50 px-2.5 py-1 text-[0.65rem] font-semibold uppercase tracking-[0.18em] text-blocks-secondary-800">
      <span className="h-1.5 w-1.5 rounded-full bg-blocks-secondary-500" />
      {eyebrow}
    </span>
    <h2 className="mt-3 text-2xl font-semibold tracking-tight text-high-emphasis">{title}</h2>
    <p className="mt-2 max-w-2xl text-sm leading-relaxed text-muted-foreground">{description}</p>
  </div>
);
