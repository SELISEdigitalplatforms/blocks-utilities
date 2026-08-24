import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { MarkPaymentFailedRequest } from "../models/subscription-simulation-harness.model";
import { subscriptionSimulationHarnessService } from "../services/subscription-simulation-harness.service";

export const useMarkPaymentFailed = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      subscriptionId,
      request,
    }: {
      subscriptionId: string;
      request: MarkPaymentFailedRequest;
    }) => subscriptionSimulationHarnessService.markPaymentFailed(subscriptionId, request),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] }),
  });
};
