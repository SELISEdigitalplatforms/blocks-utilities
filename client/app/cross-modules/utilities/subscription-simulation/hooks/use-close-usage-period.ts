import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { CloseUsagePeriodRequest } from "../models/subscription-simulation-harness.model";
import { subscriptionSimulationHarnessService } from "../services/subscription-simulation-harness.service";

export const useCloseUsagePeriod = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      subscriptionId,
      request,
    }: {
      subscriptionId: string;
      request: CloseUsagePeriodRequest;
    }) => subscriptionSimulationHarnessService.closeUsagePeriod(subscriptionId, request),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] }),
  });
};
