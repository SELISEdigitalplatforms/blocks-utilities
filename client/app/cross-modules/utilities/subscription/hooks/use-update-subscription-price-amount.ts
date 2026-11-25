import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { UpdateSubscriptionPriceAmountRequest } from "../models/subscription-plan.model";
import { subscriptionService } from "../services/subscription.service";

export const useUpdateSubscriptionPriceAmount = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      priceId,
      request,
    }: {
      priceId: string;
      request: UpdateSubscriptionPriceAmountRequest;
    }) => subscriptionService.updatePriceAmount(priceId, request),
    onSuccess: (plan) => {
      queryClient.invalidateQueries({ queryKey: ["subscription-plans"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-plan", plan.planId] });
    },
  });
};
