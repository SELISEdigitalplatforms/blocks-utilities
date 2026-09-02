import { Check } from "lucide-react";
import { useStepper } from "@/components/stepper/stepper-provider";
import { cn } from "@/lib/utils";

export interface PlanBuilderProgressProps {
  /**
   * True while the stepper is pinned to the top of the viewport. Drives the raised treatment only.
   *
   * The stuck styling is deliberately **dimension-neutral** - shadow, ring and border colour,
   * never border width or padding. The measured height of this element positions the preview
   * panel, so changing the box on stick would shift the preview every time the user crossed the
   * threshold, and could provoke scroll anchoring.
   */
  isStuck?: boolean;
}

export const PlanBuilderProgress = ({ isStuck = false }: PlanBuilderProgressProps) => {
  const { completedSteps, currentStep, getSteps, goToStep, totalSteps } = useStepper();
  const steps = getSteps();

  // The filled fraction of the rail behind the nodes. Driven by the current step rather than by
  // `completedSteps` so the bar moves the moment the user arrives somewhere, not only once the
  // step behind them is validated - which is what makes it read as position rather than score.
  const progressPercent = totalSteps > 1 ? ((currentStep - 1) / (totalSteps - 1)) * 100 : 100;

  return (
    <section
      aria-label="Plan creation progress"
      data-stuck={isStuck ? "true" : "false"}
      className={cn(
        "relative overflow-hidden rounded-2xl border bg-card/95 px-4 py-4 backdrop-blur-xl transition-[box-shadow,border-color] duration-300 supports-[backdrop-filter]:bg-card/80 sm:px-6 sm:py-5",
        isStuck
          ? "border-blocks-primary-200 shadow-lg ring-1 ring-blocks-primary-100/60"
          : "border-blocks-primary-100 shadow-sm ring-0",
      )}
    >
      {/* Brand hairline along the top edge. Decorative; clipped by the section's rounding. */}
      <span
        aria-hidden="true"
        className="pointer-events-none absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-blocks-secondary-400 to-transparent"
      />

      <div className="mb-4 flex items-center justify-between gap-4">
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.16em] text-blocks-primary-600">
            Plan setup
          </p>
          <p className="mt-1 text-sm font-medium text-foreground">
            Step {currentStep} of {totalSteps}
          </p>
        </div>
        <p className="hidden text-sm text-muted-foreground sm:block">
          Complete each section, then review your plan.
        </p>
      </div>

      <div className="relative">
        {/*
          One continuous rail behind all five nodes, rather than a separate connector per step.
          The nodes are centred in equal-width columns, so it spans from the first centre to the
          last - i.e. inset by half a column at each end.
        */}
        <div
          aria-hidden="true"
          className="absolute left-[10%] right-[10%] top-4 h-0.5 -translate-y-1/2 overflow-hidden rounded-full bg-border"
        >
          <div
            className="h-full rounded-full bg-gradient-to-r from-blocks-primary-500 to-blocks-secondary-500 transition-[width] duration-500 ease-out"
            style={{ width: `${progressPercent}%` }}
          />
        </div>

        <ol className="relative grid grid-cols-5">
          {steps.map((step) => {
            const isCurrent = currentStep === step.id;
            const isComplete = completedSteps.includes(step.id);
            const canNavigate = isCurrent || step.id === 1 || completedSteps.includes(step.id - 1);

            return (
              <li key={step.id} className="relative min-w-0">
                <button
                  type="button"
                  onClick={() => goToStep(step.id)}
                  disabled={!canNavigate}
                  aria-current={isCurrent ? "step" : undefined}
                  className="group relative z-10 flex w-full flex-col items-center gap-2 text-center disabled:cursor-not-allowed"
                >
                  <span
                    className={cn(
                      "flex h-8 w-8 items-center justify-center rounded-full border bg-card text-xs font-bold text-muted-foreground shadow-sm transition-all duration-300 ease-out",
                      isCurrent &&
                        "scale-110 border-transparent bg-gradient-to-br from-blocks-primary-500 to-blocks-primary-700 text-white shadow-[0_8px_20px_-8px_hsl(var(--blocks-primary-700)/0.9)] ring-4 ring-blocks-primary-shades-200",
                      isComplete &&
                        !isCurrent &&
                        "border-transparent bg-gradient-to-br from-blocks-secondary-500 to-blocks-secondary-700 text-white",
                      canNavigate &&
                        !isCurrent &&
                        !isComplete &&
                        "group-hover:-translate-y-0.5 group-hover:border-blocks-primary-300 group-hover:text-blocks-primary-600",
                      canNavigate && isComplete && !isCurrent && "group-hover:-translate-y-0.5",
                    )}
                  >
                    {isComplete && !isCurrent ? (
                      <Check className="h-4 w-4" />
                    ) : (
                      step.id
                    )}
                  </span>
                  <span
                    className={cn(
                      "hidden max-w-full truncate text-xs font-medium text-muted-foreground transition-colors duration-200 sm:block",
                      isComplete && !isCurrent && "text-foreground",
                      isCurrent && "font-semibold text-blocks-primary-600",
                    )}
                  >
                    {step.title}
                  </span>
                </button>
              </li>
            );
          })}
        </ol>
      </div>

      <p className="mt-3 text-center text-sm font-semibold text-blocks-primary-600 sm:hidden">
        {steps[currentStep - 1]?.title}
      </p>
    </section>
  );
};
