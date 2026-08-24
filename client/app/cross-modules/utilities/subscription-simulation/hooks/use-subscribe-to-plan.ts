import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { SubscribeToPlanRequest } from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

export const useSubscribeToPlan = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: SubscribeToPlanRequest) =>
      subscriptionSimulationService.subscribe(request),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] }),
  });
};
