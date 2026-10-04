import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { ChangeSubscriptionPlanRequest } from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

export const useChangeSubscriptionPlan = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      subscriptionId,
      request,
    }: {
      subscriptionId: string;
      request: ChangeSubscriptionPlanRequest;
    }) => subscriptionSimulationService.changePlan(subscriptionId, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-entitlements"] });
      // An immediate change starts a new period, and a per-person subscription's places each
      // open a fresh window with it: the Members card kept the old period's figures until reload.
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-members"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-usage"] });
    },
  });
};

/**
 * Withdraws a plan change booked for the end of the paid period.
 *
 * Invalidates the same two queries a plan change does: the booking is gone, so the current
 * subscription reads differently — even though nothing about what is granted today moved.
 */
export const useCancelPendingPlanChange = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      subscriptionId,
      organizationId,
    }: {
      subscriptionId: string;
      organizationId?: string;
    }) =>
      subscriptionSimulationService.cancelPendingPlanChange(subscriptionId, organizationId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-entitlements"] });
    },
  });
};
