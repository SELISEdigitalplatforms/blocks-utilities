import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import { formatInterval } from "../../subscription/utilities/subscription-format";
import type {
  PendingPlanChange,
  PlanChangeLabel,
  SimulatedSubscription,
} from "../models/subscription-simulation.model";

/**
 * What a booked change moves to: the plan, or — when only the billing changes — the new cadence.
 * "Moving to Pro" on a subscription already on Pro read as though nothing were changing.
 */
export const describePendingMove = (
  subscription: Pick<SimulatedSubscription, "planCode">,
  pending: PendingPlanChange,
): string =>
  pending.targetPlanCode === subscription.planCode
    ? `billing ${formatInterval(pending.interval, pending.intervalCount)}`
    : pending.targetPlanName;

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
