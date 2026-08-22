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
    },
  });
};
