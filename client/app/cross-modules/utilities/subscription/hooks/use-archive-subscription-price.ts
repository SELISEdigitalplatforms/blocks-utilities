import { useMutation, useQueryClient } from "@tanstack/react-query";
import { subscriptionService } from "../services/subscription.service";

export const useArchiveSubscriptionPrice = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      priceId,
      organizationId,
    }: {
      priceId: string;
      organizationId?: string;
    }) => subscriptionService.archivePrice(priceId, organizationId),
    onSuccess: (plan) => {
      queryClient.invalidateQueries({ queryKey: ["subscription-plans"] });
      queryClient.invalidateQueries({
        queryKey: ["subscription-plan", plan.planId],
      });
    },
  });
};
