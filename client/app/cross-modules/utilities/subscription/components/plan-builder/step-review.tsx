import { PlanSummaryCard, type PlanSummaryData } from "../plan-summary-card";
import { StepHeading } from "./step-heading";

export const StepReview = ({ plan }: { plan: PlanSummaryData }) => (
  <div className="space-y-6">
    <StepHeading
      eyebrow="Review"
      title="Review"
      description="This is exactly how the plan will read once it's created."
    />

    <PlanSummaryCard plan={plan} />
  </div>
);
