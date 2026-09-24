import { ArrowLeft, ArrowRight, Check, Loader2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { cn } from "@/lib/utils";
import { useStickyActionBar } from "../plan-builder/use-sticky-action-bar";

export interface CampaignBuilderActionsProps {
  isFirstStep: boolean;
  isLastStep: boolean;
  isSubmitting: boolean;
  canAdvance: boolean;
  editing: boolean;
  onCancel: () => void;
  onBack: () => void;
  onNext: () => void;
  onSubmit: () => void;
}

/**
 * Cancel/Back + Next/Submit for {@link CampaignBuilder}, pinned to the bottom of the viewport
 * while the step card is taller than the screen — same sticky treatment as
 * `plan-builder/plan-builder-actions.tsx`, but with Cancel on step 1 and a disabled Next when the
 * current step still has problems (PlanBuilderActions hard-codes Back and never disables Next).
 */
export const CampaignBuilderActions = ({
  isFirstStep,
  isLastStep,
  isSubmitting,
  canAdvance,
  editing,
  onCancel,
  onBack,
  onNext,
  onSubmit,
}: CampaignBuilderActionsProps) => {
  const { barRef, isStuck } = useStickyActionBar();

  const submitLabel = editing ? "Save changes" : "Create discount";
  const submittingLabel = editing ? "Saving…" : "Creating…";

  return (
    <div
      ref={barRef}
      data-stuck={isStuck ? "true" : "false"}
      data-testid="campaign-builder-actions"
      className="sticky bottom-0 z-20 -mx-5 -mb-5 mt-8 sm:-mx-7 sm:-mb-7"
    >
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
          onClick={isFirstStep ? onCancel : onBack}
          disabled={isSubmitting}
          className="group rounded-xl border-border/70 transition-all duration-200 hover:-translate-x-0.5 hover:border-blocks-primary-300 hover:bg-blocks-primary-shades-200 hover:text-blocks-primary-600 disabled:hover:translate-x-0"
        >
          <ArrowLeft className="mr-2 h-4 w-4 transition-transform duration-200 group-hover:-translate-x-0.5" />
          {isFirstStep ? "Cancel" : "Back"}
        </Button>

        {isLastStep ? (
          <Button
            type="button"
            onClick={onSubmit}
            disabled={isSubmitting}
            className="group relative overflow-hidden rounded-xl bg-gradient-to-br from-blocks-primary-500 to-blocks-primary-700 px-6 text-white shadow-[0_10px_30px_-12px_hsl(var(--blocks-primary-700)/0.9)] transition-all duration-200 hover:-translate-y-0.5 hover:shadow-[0_16px_38px_-12px_hsl(var(--blocks-primary-700)/0.95)] disabled:hover:translate-y-0"
          >
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
            disabled={!canAdvance || isSubmitting}
            className="group rounded-xl bg-gradient-to-br from-blocks-primary-500 to-blocks-primary-700 px-6 text-white shadow-[0_10px_30px_-12px_hsl(var(--blocks-primary-700)/0.9)] transition-all duration-200 hover:-translate-y-0.5 hover:shadow-[0_16px_38px_-12px_hsl(var(--blocks-primary-700)/0.95)] disabled:hover:translate-y-0"
          >
            Next
            <ArrowRight className="ml-2 h-4 w-4 transition-transform duration-200 group-hover:translate-x-0.5" />
          </Button>
        )}
      </div>
    </div>
  );
};
