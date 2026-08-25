import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { UpdateSubscriptionPriceDiscountRequest } from "../models/subscription-plan.model";
import { subscriptionService } from "../services/subscription.service";

export const useUpdateSubscriptionPriceDiscount = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      priceId,
      request,
    }: {
      priceId: string;
      request: UpdateSubscriptionPriceDiscountRequest;
    }) => subscriptionService.updatePriceDiscount(priceId, request),
    onSuccess: (plan) => {
      queryClient.invalidateQueries({ queryKey: ["subscription-plans"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-plan", plan.planId] });
    },
  });
};
