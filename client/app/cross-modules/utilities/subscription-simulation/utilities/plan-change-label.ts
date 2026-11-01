import type { SubscriptionPlan } from "../../subscription/models/subscription-plan.model";
import { formatInterval } from "../../subscription/utilities/subscription-format";
import type {
  PendingPlanChange,
  PlanChangeLabel,
  SimulatedSubscription,
} from "../models/subscription-simulation.model";
import { tStatic } from "@seliseblocks/genesis-os";

/**
 * What a booked change moves to: the plan, or — when only the billing changes — the new cadence.
 * "Moving to Pro" on a subscription already on Pro read as though nothing were changing.
 */
export const describePendingMove = (
  subscription: Pick<SimulatedSubscription, "planCode">,
  pending: PendingPlanChange,
): string =>
  pending.targetPlanCode === subscription.planCode
    ? tStatic("utilities.subscription-simulation", "PLAN_CHANGE_LABEL.BILLING_INTERVAL", {
        interval: formatInterval(pending.interval, pending.intervalCount),
      })
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
    return tStatic("utilities.subscription-simulation", "PLAN_CHANGE_LABEL.CHANGE_BILLING_CADENCE");
  }

  if (
    currentPlan?.familyCode &&
    targetPlan.familyCode &&
    currentPlan.familyCode === targetPlan.familyCode
  ) {
    const currentRank = currentPlan.familyRank ?? 0;
    const targetRank = targetPlan.familyRank ?? 0;

    if (targetRank > currentRank) {
      return tStatic("utilities.subscription-simulation", "PLAN_CHANGE_LABEL.UPGRADE");
    }
    if (targetRank < currentRank) {
      return tStatic("utilities.subscription-simulation", "PLAN_CHANGE_LABEL.DOWNGRADE");
    }
    return tStatic("utilities.subscription-simulation", "PLAN_CHANGE_LABEL.CHANGE_BILLING_CADENCE");
  }

  return tStatic("utilities.subscription-simulation", "PLAN_CHANGE_LABEL.SWITCH_PLAN");
};
