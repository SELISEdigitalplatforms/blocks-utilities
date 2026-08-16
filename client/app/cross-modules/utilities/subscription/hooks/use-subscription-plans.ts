import { useProjectStore } from "@seliseblocks/genesis-os";
import { useQuery } from "@tanstack/react-query";
import { subscriptionService } from "../services/subscription.service";

export const useSubscriptionPlans = (organizationId?: string) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    queryKey: ["subscription-plans", tenantId, organizationId ?? null],
    queryFn: () => subscriptionService.listPlans(organizationId),
    staleTime: 30_000,
  });
};
