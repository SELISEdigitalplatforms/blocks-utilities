import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { ChangeSubscriptionPlanRequest } from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

export const useChangeSubscriptionPlan = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      subscriptionId,
      request,
      organizationId,
    }: {
      subscriptionId: string;
      request: ChangeSubscriptionPlanRequest;
      organizationId?: string;
    }) => subscriptionSimulationService.changePlan(subscriptionId, request, organizationId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-entitlements"] });
    },
  });
};
