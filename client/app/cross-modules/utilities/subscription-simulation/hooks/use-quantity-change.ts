import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { ChangeQuantityRequest } from "../models/subscription-simulation.model";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

/**
 * What a quantity change would cost. Writes nothing, so it invalidates nothing.
 */
export const usePreviewQuantityChange = () =>
  useMutation({
    mutationFn: ({
      subscriptionId,
      request,
    }: {
      subscriptionId: string;
      request: ChangeQuantityRequest;
    }) => subscriptionSimulationService.previewQuantityChange(subscriptionId, request),
  });

/**
 * Applies the change.
 *
 * The subscription is re-read on success rather than patched from the response: an increase moves
 * the version twice — once to reserve the units, once to grant them — and entitlement is derived
 * from the quantity, so both have to come from the server rather than be inferred here.
 */
export const useChangeQuantity = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      subscriptionId,
      request,
    }: {
      subscriptionId: string;
      request: ChangeQuantityRequest;
    }) => subscriptionSimulationService.changeQuantity(subscriptionId, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-entitlements"] });
    },
  });
};

/** Withdraws a scheduled decrease. */
export const useCancelPendingQuantityChange = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      subscriptionId,
      organizationId,
    }: {
      subscriptionId: string;
      organizationId?: string;
    }) =>
      subscriptionSimulationService.cancelPendingQuantityChange(subscriptionId, organizationId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-entitlements"] });
    },
  });
};
