import { useProjectStore } from "@seliseblocks/genesis-os";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { UpdateBillingProfileRequest } from "../models/subscription-billing.model";
import { subscriptionBillingService } from "../services/subscription-billing.service";

const profileKey = (tenantId: string, organizationId?: string) =>
  ["subscription-billing-profile", tenantId, organizationId ?? null] as const;

export const useBillingProfile = (organizationId?: string) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    queryKey: profileKey(tenantId, organizationId),
    queryFn: () => subscriptionBillingService.getBillingProfile(organizationId),
    // Short, because the answer gates checkout: a subscriber who has just completed the form must
    // not be told for another minute that it is still incomplete.
    staleTime: 5_000,
  });
};

export const useUpdateBillingProfile = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: UpdateBillingProfileRequest) =>
      subscriptionBillingService.updateBillingProfile(request),
    onSuccess: () => {
      // Invalidated by prefix rather than by exact key: the profile gates operations rendered on
      // other pages, and a stale "incomplete" banner is worse than a refetch.
      queryClient.invalidateQueries({ queryKey: ["subscription-billing-profile"] });
    },
  });
};
