import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { RecordUsageRequest } from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

export const useRecordUsage = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: RecordUsageRequest) =>
      subscriptionSimulationService.recordUsage(request),
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: ["subscription-simulation-entitlements"],
      }),
  });
};
