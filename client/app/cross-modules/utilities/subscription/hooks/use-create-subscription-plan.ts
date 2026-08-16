import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { CreateSubscriptionPlanRequest } from "../models/subscription-plan.model";
import { subscriptionService } from "../services/subscription.service";

export const useCreateSubscriptionPlan = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateSubscriptionPlanRequest) =>
      subscriptionService.createPlan(request),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["subscription-plans"] }),
  });
};
