import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import type { PlanChangeLabel } from "../models/subscription-simulation.model";

/**
 * Labelled from product family metadata, not price — a lower amount is not necessarily a
 * downgrade once discounts, quantities and monthly-versus-annual billing are in play.
 */
export const labelPlanChange = (
  currentPlan: SubscriptionPlan | undefined,
  targetPlan: SubscriptionPlan,
): PlanChangeLabel => {
  if (currentPlan && currentPlan.planId === targetPlan.planId) {
    return "Change billing cadence";
  }

  if (
    currentPlan?.familyCode &&
    targetPlan.familyCode &&
    currentPlan.familyCode === targetPlan.familyCode
  ) {
    const currentRank = currentPlan.familyRank ?? 0;
    const targetRank = targetPlan.familyRank ?? 0;

    if (targetRank > currentRank) {
      return "Upgrade";
    }
    if (targetRank < currentRank) {
      return "Downgrade";
    }
    return "Change billing cadence";
  }

  return "Switch plan";
};
