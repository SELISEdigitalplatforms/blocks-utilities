import { ArrowLeft, ArrowRight, Check, Loader2 } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import StepperProviderComponent, { useStepper } from "@/components/stepper/stepper-provider";
import type { Steps } from "@/components/stepper/stepper-models";
import type { SubscriptionPlan } from "../../models/subscription-plan.model";
import { CampaignBuilderProgress } from "./campaign-builder-progress";
import {
  EMPTY_DRAFT,
  stepProblems,
  toCreateDiscountRequest,
  withCampaignKind,
  type CampaignDraft,
  type StepId,
} from "./campaign-draft";
import { StepBenefit } from "./step-benefit";
import { StepEligibility } from "./step-eligibility";
import { StepIdentity } from "./step-identity";
import { StepReview } from "./step-review";

const STEPS: Steps = [
  { id: 1, title: "Identity" },
  { id: 2, title: "Benefit" },
  { id: 3, title: "Eligibility" },
  { id: 4, title: "Review" },
];

export interface CampaignBuilderProps {
  plans: SubscriptionPlan[];
  organizationId: string | undefined;
  isSubmitting: boolean;
  submissionError: string | null;
  /** Rejecting leaves the draft in place and shows the error; the caller navigates on success. */
  onSubmit: (draft: CampaignDraft, organizationId: string | undefined) => Promise<void>;
  onCancel: () => void;
  initialDraft?: CampaignDraft;
  editing?: boolean;
}

export const CampaignBuilder = (props: CampaignBuilderProps) => (
  <StepperProviderComponent steps={STEPS}>
    <CampaignBuilderWizard {...props} />
  </StepperProviderComponent>
);

const CampaignBuilderWizard = ({
  plans,
  organizationId,
  isSubmitting,
  submissionError,
  onSubmit,
  onCancel,
  initialDraft = EMPTY_DRAFT,
  editing = false,
}: CampaignBuilderProps) => {
  const { currentStep, nextStep, previousStep } = useStepper();
  const [draft, setDraft] = useState<CampaignDraft>(initialDraft);
  const step = currentStep as StepId;
  const isLastStep = currentStep === STEPS.length;

  const update = (next: Partial<CampaignDraft>) => {
    // Picking a new offer type locks in that kind's own rules immediately (a full 100% reduction
    // and its two required flags for a free month, a cleared entitlement override for a
    // first-year discount) -- see withCampaignKind. Every other field is a plain merge.
    setDraft((current) =>
      next.campaignKind !== undefined ? withCampaignKind(current, next.campaignKind) : { ...current, ...next },
    );
  };

  const problems = stepProblems(step, draft, plans);
  const canAdvance = problems.length === 0;

  const submit = async () => {
    if (stepProblems(1, draft, plans).length > 0 ||
        stepProblems(2, draft, plans).length > 0 ||
        stepProblems(3, draft, plans).length > 0) {
      return;
    }

    await onSubmit(draft, organizationId);
  };

  return (
    <div className="space-y-5">
      <CampaignBuilderProgress />

      <Card className="space-y-5 rounded-2xl p-5 sm:p-7">
        {step === 1 && <StepIdentity draft={draft} onChange={update} codeReadOnly={editing} />}
        {step === 2 && <StepBenefit draft={draft} onChange={update} />}
        {step === 3 && <StepEligibility draft={draft} plans={plans} onChange={update} />}
        {step === 4 && <StepReview draft={draft} plans={plans} />}

        {!isLastStep && problems.length > 0 && (
          <ul className="space-y-1 rounded-md border border-destructive/20 bg-destructive/5 p-3 text-xs text-destructive">
            {problems.map((problem) => (
              <li key={problem}>{problem}</li>
            ))}
          </ul>
        )}

        {submissionError && (
          <div
            role="alert"
            className="rounded-md border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm font-medium text-destructive"
          >
            {submissionError}
          </div>
        )}

        <div className="flex justify-between border-t pt-5">
          <Button
            type="button"
            variant="outline"
            onClick={currentStep === 1 ? onCancel : previousStep}
            disabled={isSubmitting}
          >
            <ArrowLeft className="mr-2 h-4 w-4" />
            {currentStep === 1 ? "Cancel" : "Back"}
          </Button>

          {isLastStep ? (
            <Button type="button" onClick={submit} disabled={isSubmitting}>
              {isSubmitting ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Check className="mr-2 h-4 w-4" />}
              {isSubmitting ? "Creating…" : "Create discount"}
            </Button>
          ) : (
            <Button type="button" onClick={nextStep} disabled={!canAdvance}>
              Next
              <ArrowRight className="ml-2 h-4 w-4" />
            </Button>
          )}
        </div>
      </Card>
    </div>
  );
};
