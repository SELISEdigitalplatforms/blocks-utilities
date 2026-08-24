import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { UpdateDataFieldRequest } from "../models/subscription-simulation-harness.model";
import { subscriptionSimulationHarnessService } from "../services/subscription-simulation-harness.service";

export const useUpdateDataFields = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      subscriptionId,
      logicalCollection,
      request,
    }: {
      subscriptionId: string;
      logicalCollection: string;
      request: UpdateDataFieldRequest;
    }) =>
      subscriptionSimulationHarnessService.updateData(subscriptionId, logicalCollection, request),
    // A field write can touch the same document the integrator-facing read shows (e.g.
    // NextFeeBillingAtUtc on `subscriptions`), so the current-subscription panel should not go
    // stale after one.
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] }),
  });
};
