import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { CancelSubscriptionRequest } from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

export const useCancelSubscription = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CancelSubscriptionRequest) =>
      subscriptionSimulationService.cancel(request),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] }),
  });
};

/**
 * Undoes a cancellation scheduled for the end of the paid period, restoring the renewal it
 * cleared. Invalidates the same two queries a plan change does: what is granted today has not
 * moved, but the current subscription and its entitlements both read differently once the
 * schedule is gone.
 */
export const useWithdrawCancellation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      subscriptionId,
      organizationId,
    }: {
      subscriptionId: string;
      organizationId?: string;
    }) => subscriptionSimulationService.withdrawCancellation(subscriptionId, organizationId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-entitlements"] });
    },
  });
};
