import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { AdvanceRenewalRequest } from "../models/subscription-simulation-harness.model";
import { subscriptionSimulationHarnessService } from "../services/subscription-simulation-harness.service";

export const useAdvanceRenewal = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      subscriptionId,
      request,
    }: {
      subscriptionId: string;
      request: AdvanceRenewalRequest;
    }) => subscriptionSimulationHarnessService.advanceRenewal(subscriptionId, request),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] }),
  });
};
