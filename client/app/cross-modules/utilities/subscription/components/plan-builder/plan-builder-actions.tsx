import { ArrowLeft, ArrowRight, Check, Loader2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { cn } from "@/lib/utils";
import { useStickyActionBar } from "./use-sticky-action-bar";

export interface PlanBuilderActionsProps {
  isFirstStep: boolean;
  isLastStep: boolean;
  isSubmitting: boolean;
  submitLabel: string;
  submittingLabel: string;
  onBack: () => void;
  onNext: () => void;
  onSubmit: () => void;
}

/**
 * Back / Next / Confirm, pinned to the bottom of the viewport for as long as the step it belongs
 * to is taller than the screen.
 *
 * It sits inside the step card rather than floating over the whole page, which is what keeps it
 * aligned with the form column instead of running under the preview panel at xl. The negative
 * margins let it span the card's padding so the bar reads as an edge-to-edge footer, and the
 * card's own bottom padding is what it settles into once the step is fully scrolled.
 *
 * `z-20` for the same reason the stepper is `z-30`: both must stay below the z-50 Radix
 * Select/Popover portals, or an open dropdown near the foot of a step would render behind the bar.
 */
export const PlanBuilderActions = ({
  isFirstStep,
  isLastStep,
  isSubmitting,
  submitLabel,
  submittingLabel,
  onBack,
  onNext,
  onSubmit,
}: PlanBuilderActionsProps) => {
  const { barRef, isStuck } = useStickyActionBar();

  return (
    <div
      ref={barRef}
      data-stuck={isStuck ? "true" : "false"}
      data-testid="plan-builder-actions"
      className="sticky bottom-0 z-20 -mx-5 -mb-5 mt-8 sm:-mx-7 sm:-mb-7"
    >
      {/*
        Dissolves the last line of the step into the bar instead of letting it slide under a hard
        edge. Purely decorative, and outside the bar's own box so it never affects its height.
      */}
      <div
        aria-hidden="true"
        className={cn(
          "pointer-events-none absolute inset-x-0 bottom-full h-10 bg-gradient-to-t from-card to-transparent transition-opacity duration-300",
          isStuck ? "opacity-100" : "opacity-0",
        )}
      />

      <div
        className={cn(
          "flex items-center justify-between gap-3 rounded-b-2xl border-t px-5 py-4 transition-[background-color,border-color,box-shadow] duration-300 sm:px-7",
          "bg-card/85 backdrop-blur-xl supports-[backdrop-filter]:bg-card/70",
          isStuck
            ? "border-blocks-primary-200/80 shadow-[0_-20px_45px_-30px_hsl(var(--blocks-primary-900)/0.55)]"
            : "border-border/60 shadow-none",
        )}
      >
        <Button
          type="button"
          variant="outline"
          onClick={onBack}
          disabled={isFirstStep || isSubmitting}
          className="group rounded-xl border-border/70 transition-all duration-200 hover:-translate-x-0.5 hover:border-blocks-primary-300 hover:bg-blocks-primary-shades-200 hover:text-blocks-primary-600 disabled:hover:translate-x-0"
        >
          <ArrowLeft className="mr-2 h-4 w-4 transition-transform duration-200 group-hover:-translate-x-0.5" />
          Back
        </Button>

        {isLastStep ? (
          <Button
            type="button"
            onClick={onSubmit}
            disabled={isSubmitting}
            className="group relative overflow-hidden rounded-xl bg-gradient-to-br from-blocks-primary-500 to-blocks-primary-700 px-6 text-white shadow-[0_10px_30px_-12px_hsl(var(--blocks-primary-700)/0.9)] transition-all duration-200 hover:-translate-y-0.5 hover:shadow-[0_16px_38px_-12px_hsl(var(--blocks-primary-700)/0.95)] disabled:hover:translate-y-0"
          >
            {/* A sheen that crosses the button on hover. Decorative, and clipped by the button. */}
            <span
              aria-hidden="true"
              className="pointer-events-none absolute inset-y-0 -left-full w-1/2 skew-x-[-20deg] bg-white/20 transition-all duration-700 group-hover:left-[150%]"
            />
            {isSubmitting ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <Check className="mr-2 h-4 w-4" />
            )}
            {isSubmitting ? submittingLabel : submitLabel}
          </Button>
        ) : (
          <Button
            type="button"
            onClick={onNext}
            className="group rounded-xl bg-gradient-to-br from-blocks-primary-500 to-blocks-primary-700 px-6 text-white shadow-[0_10px_30px_-12px_hsl(var(--blocks-primary-700)/0.9)] transition-all duration-200 hover:-translate-y-0.5 hover:shadow-[0_16px_38px_-12px_hsl(var(--blocks-primary-700)/0.95)]"
          >
            Next
            <ArrowRight className="ml-2 h-4 w-4 transition-transform duration-200 group-hover:translate-x-0.5" />
          </Button>
        )}
      </div>
    </div>
  );
};
