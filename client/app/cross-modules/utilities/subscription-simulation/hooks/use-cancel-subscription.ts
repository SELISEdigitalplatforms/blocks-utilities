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
