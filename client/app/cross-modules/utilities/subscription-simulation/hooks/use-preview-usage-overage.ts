import { useMutation } from "@tanstack/react-query";
import type { PreviewUsageOverageRequest } from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

/**
 * What additional metered usage would cost. Writes nothing, so it invalidates nothing — the
 * server computes every price; this hook only carries the request and hands back the response.
 */
export const usePreviewUsageOverage = () =>
  useMutation({
    mutationFn: (request: PreviewUsageOverageRequest) =>
      subscriptionSimulationService.previewUsageOverage(request),
  });
