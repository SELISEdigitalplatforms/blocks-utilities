import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { RunDueJobsRequest } from "../models/subscription-simulation-harness.model";
import { subscriptionSimulationHarnessService } from "../services/subscription-simulation-harness.service";

export const useRunDueJobs = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      subscriptionId,
      request,
    }: {
      subscriptionId: string;
      request: RunDueJobsRequest;
    }) => subscriptionSimulationHarnessService.runDueJobs(subscriptionId, request),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] }),
  });
};
