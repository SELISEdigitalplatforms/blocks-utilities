import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { UpdateSubscriptionPlanRequest } from "../models/subscription-plan.model";
import { subscriptionService } from "../services/subscription.service";

export const useUpdateSubscriptionPlan = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      planId,
      request,
    }: {
      planId: string;
      request: UpdateSubscriptionPlanRequest;
    }) => subscriptionService.updatePlan(planId, request),
    onSuccess: (plan) => {
      queryClient.invalidateQueries({ queryKey: ["subscription-plans"] });
      queryClient.invalidateQueries({
        queryKey: ["subscription-plan", plan.planId],
      });
    },
  });
};
