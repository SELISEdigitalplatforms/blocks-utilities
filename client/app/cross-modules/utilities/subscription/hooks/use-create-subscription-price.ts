import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { CreateSubscriptionPriceRequest } from "../models/subscription-plan.model";
import { subscriptionService } from "../services/subscription.service";

export const useCreateSubscriptionPrice = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateSubscriptionPriceRequest) =>
      subscriptionService.createPrice(request),
    onSuccess: (plan) => {
      queryClient.invalidateQueries({ queryKey: ["subscription-plans"] });
      queryClient.invalidateQueries({
        queryKey: ["subscription-plan", plan.planId],
      });
    },
  });
};
