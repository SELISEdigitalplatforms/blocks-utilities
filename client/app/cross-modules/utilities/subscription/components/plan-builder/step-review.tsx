import { useT } from "@seliseblocks/genesis-os";
import { PlanSummaryCard, type PlanSummaryData } from "../plan-summary-card";
import { StepHeading } from "./step-heading";

export const StepReview = ({ plan }: { plan: PlanSummaryData }) => {
  const { t } = useT("utilities.subscription.plan-builder");

  return (
    <div className="space-y-6">
      <StepHeading
        eyebrow={t("STEP_REVIEW.TITLE")}
        title={t("STEP_REVIEW.TITLE")}
        description={t("STEP_REVIEW.DESCRIPTION")}
      />

      <PlanSummaryCard plan={plan} />
    </div>
  );
};
