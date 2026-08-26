import { useProjectStore } from "@seliseblocks/genesis-os";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { UpdateMerchantProfileRequest } from "../models/subscription-billing.model";
import { subscriptionBillingService } from "../services/subscription-billing.service";

/**
 * Keyed on the tenant alone. The seller is the tenant, so an organization in the key would cache one
 * organization's view of an identity that is not theirs to have.
 */
const merchantKey = (tenantId: string) =>
  ["subscription-merchant-profile", tenantId] as const;

export const useMerchantProfile = () => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    queryKey: merchantKey(tenantId),
    queryFn: () => subscriptionBillingService.getMerchantProfile(),
    // Short, for the same reason the subscriber profile's is: this gates paid subscriptions, and a
    // console that has just filled it in must not be told for another minute that it is still
    // missing.
    staleTime: 5_000,
  });
};

export const useUpdateMerchantProfile = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: UpdateMerchantProfileRequest) =>
      subscriptionBillingService.updateMerchantProfile(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["subscription-merchant-profile"] });

      // The subscriber profile's completeness answer includes the seller's, because a charge needs
      // both. Naming a seller can therefore unblock a checkout that a moment ago was refused, and a
      // stale "incomplete" banner is worse than a refetch.
      queryClient.invalidateQueries({ queryKey: ["subscription-billing-profile"] });
    },
  });
};
