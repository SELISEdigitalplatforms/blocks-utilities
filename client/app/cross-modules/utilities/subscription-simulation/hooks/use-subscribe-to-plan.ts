import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { SubscribeToPlanRequest } from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

export const useSubscribeToPlan = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ request, offline }: { request: SubscribeToPlanRequest; offline: boolean }) =>
      subscriptionSimulationService.subscribe(request, offline),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] }),
  });
};
