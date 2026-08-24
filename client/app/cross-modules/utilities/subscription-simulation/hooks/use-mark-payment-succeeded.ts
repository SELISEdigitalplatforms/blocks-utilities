import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { MarkPaymentSucceededRequest } from "../models/subscription-simulation-harness.model";
import { subscriptionSimulationHarnessService } from "../services/subscription-simulation-harness.service";

export const useMarkPaymentSucceeded = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      subscriptionId,
      request,
    }: {
      subscriptionId: string;
      request: MarkPaymentSucceededRequest;
    }) => subscriptionSimulationHarnessService.markPaymentSucceeded(subscriptionId, request),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] }),
  });
};
