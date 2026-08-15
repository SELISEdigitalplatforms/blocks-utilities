import { PlanSummaryCard, type PlanSummaryData } from "../plan-summary-card";

export const StepReview = ({ plan }: { plan: PlanSummaryData }) => (
  <div className="space-y-5">
    <div>
      <h2 className="text-lg font-semibold">Review</h2>
      <p className="mt-1 text-sm text-muted-foreground">
        This is exactly how the plan will read once it's created.
      </p>
    </div>

    <PlanSummaryCard plan={plan} />
  </div>
);
