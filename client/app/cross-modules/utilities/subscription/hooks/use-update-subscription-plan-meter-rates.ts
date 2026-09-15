import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { UpdateSubscriptionPlanMeterRatesRequest } from "../models/subscription-plan.model";
import { subscriptionService } from "../services/subscription.service";

export const useUpdateSubscriptionPlanMeterRates = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      planId,
      meterKey,
      request,
    }: {
      planId: string;
      meterKey: string;
      request: UpdateSubscriptionPlanMeterRatesRequest;
    }) => subscriptionService.updatePlanMeterRates(planId, meterKey, request),
    onSuccess: (plan) => {
      queryClient.invalidateQueries({ queryKey: ["subscription-plans"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-plan", plan.planId] });
    },
  });
};
