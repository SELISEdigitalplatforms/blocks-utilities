import { useMutation, useQueryClient } from "@tanstack/react-query";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

/**
 * Opens a card-collection session against an existing subscription and hands back the checkout
 * URL to send the subscriber to.
 *
 * Not a payment. Nothing here charges anything or produces an invoice -- it stores a card, and
 * what happens next (nothing, for a trial in progress; an immediate charge, for a recovering
 * Unpaid subscription) is decided server-side by the subscription's own status once the provider
 * confirms the setup, not by this call.
 */
export const useStartPaymentMethodSetup = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      subscriptionId,
      organizationId,
    }: {
      subscriptionId: string;
      organizationId?: string;
    }) => subscriptionSimulationService.startPaymentMethodSetup(subscriptionId, organizationId),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] }),
  });
};
