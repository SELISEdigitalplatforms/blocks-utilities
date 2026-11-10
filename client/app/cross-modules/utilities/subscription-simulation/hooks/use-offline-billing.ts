import { useMutation, useQueryClient } from "@tanstack/react-query";
import type {
  ActivateOfflineSubscriptionRequest,
  OfflineRepricingRequest,
} from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

const CURRENT_KEY = ["subscription-simulation-current"];

export const useActivateOfflineSubscription = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: ActivateOfflineSubscriptionRequest) =>
      subscriptionSimulationService.activateOffline(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CURRENT_KEY }),
  });
};

export const useRepriceOfflineSubscription = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ subscriptionId, ...request }: OfflineRepricingRequest & { subscriptionId: string }) =>
      subscriptionSimulationService.repriceOffline(subscriptionId, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CURRENT_KEY }),
  });
};
