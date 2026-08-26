import { useMutation } from "@tanstack/react-query";
import type { SubscribeToPlanRequest } from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

/**
 * What subscribing would cost right now. Writes nothing, so it invalidates nothing.
 */
export const usePreviewSubscription = () =>
  useMutation({
    mutationFn: (request: SubscribeToPlanRequest) =>
      subscriptionSimulationService.previewSubscription(request),
  });
