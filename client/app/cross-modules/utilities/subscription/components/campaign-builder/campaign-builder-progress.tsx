import { Check } from "lucide-react";
import { useStepper } from "@/components/stepper/stepper-provider";
import { cn } from "@/lib/utils";

/**
 * The four-step progress bar for {@link CampaignBuilder}. A smaller cousin of
 * `plan-builder/plan-builder-progress.tsx` — same interaction and visual language, without that
 * one's sticky-scroll and live-preview-panel treatment, which this shorter wizard has no room or
 * need for.
 */
export const CampaignBuilderProgress = () => {
  const { completedSteps, currentStep, getSteps, goToStep, totalSteps } = useStepper();
  const steps = getSteps();

  return (
    <section
      aria-label="Discount creation progress"
      className="rounded-2xl border border-blocks-primary-100 bg-card/95 px-4 py-4 shadow-sm sm:px-6 sm:py-5"
    >
      <p className="mb-4 text-sm font-medium text-foreground">
        Step {currentStep} of {totalSteps}
      </p>

      <ol className="grid grid-cols-4">
        {steps.map((step, index) => {
          const isCurrent = currentStep === step.id;
          const isComplete = completedSteps.includes(step.id);
          const canNavigate = isCurrent || step.id === 1 || completedSteps.includes(step.id - 1);

          return (
            <li
              key={step.id}
              className={cn(
                "relative min-w-0",
                index < steps.length - 1 &&
                  "after:absolute after:left-[calc(50%+1.25rem)] after:right-[calc(-50%+1.25rem)] after:top-4 after:h-0.5 after:bg-border after:content-['']",
                index < steps.length - 1 && isComplete && "after:bg-blocks-primary-500",
              )}
            >
              <button
                type="button"
                onClick={() => goToStep(step.id)}
                disabled={!canNavigate}
                aria-current={isCurrent ? "step" : undefined}
                className="group relative z-10 flex w-full flex-col items-center gap-2 text-center disabled:cursor-not-allowed"
              >
                <span
                  className={cn(
                    "flex h-8 w-8 items-center justify-center rounded-full border bg-card text-xs font-bold text-muted-foreground transition-all duration-200",
                    isCurrent &&
                      "border-blocks-primary-600 bg-blocks-primary-600 text-white ring-4 ring-blocks-primary-100",
                    isComplete &&
                      !isCurrent &&
                      "border-blocks-primary-500 bg-blocks-primary-50 text-blocks-primary-700",
                    canNavigate &&
                      !isCurrent &&
                      "group-hover:border-blocks-primary-400 group-hover:text-blocks-primary-700",
                  )}
                >
                  {isComplete && !isCurrent ? <Check className="h-4 w-4" /> : step.id}
                </span>
                <span
                  className={cn(
                    "hidden max-w-full truncate text-xs font-medium text-muted-foreground transition-colors sm:block",
                    (isCurrent || isComplete) && "text-foreground",
                  )}
                >
                  {step.title}
                </span>
              </button>
            </li>
          );
        })}
      </ol>

      <p className="mt-3 text-center text-sm font-medium text-foreground sm:hidden">
        {steps[currentStep - 1]?.title}
      </p>
    </section>
  );
};
