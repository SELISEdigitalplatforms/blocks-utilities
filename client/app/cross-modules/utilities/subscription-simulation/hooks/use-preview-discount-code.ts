import { useMutation } from "@tanstack/react-query";
import type { SubscribeToPlanRequest } from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

/**
 * Checks one discount code against a plan and price. Reserves no redemption and writes nothing,
 * so it invalidates nothing.
 */
export const usePreviewDiscountCode = () =>
  useMutation({
    mutationFn: (request: SubscribeToPlanRequest) =>
      subscriptionSimulationService.previewDiscountCode(request),
  });
