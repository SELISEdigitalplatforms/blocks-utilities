import { useMutation } from "@tanstack/react-query";
import type { ChangeSubscriptionPlanRequest } from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

/**
 * What moving to another plan or price would cost. Writes nothing, so it invalidates nothing.
 */
export const usePreviewPlanChange = () =>
  useMutation({
    mutationFn: ({
      subscriptionId,
      request,
    }: {
      subscriptionId: string;
      request: ChangeSubscriptionPlanRequest;
    }) => subscriptionSimulationService.previewPlanChange(subscriptionId, request),
  });
