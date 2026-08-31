import { useProjectStore } from "@seliseblocks/genesis-os";
import { useQuery } from "@tanstack/react-query";
import type { PlanCatalogueFilterName } from "../models/subscription-plan.model";
import { subscriptionService } from "../services/subscription.service";

/**
 * The plans a screen may show.
 *
 * @param status Which plans to ask for, defaulting to the active catalogue. Only the management
 * catalogue passes anything here. Every other caller — the discount authoring page, the subscribe
 * and change-plan dialogs — omits it and therefore cannot show an archived plan even by accident,
 * which is a stronger guarantee than each of them filtering for itself.
 *
 * The status is part of the query key, so switching filters is a separate cached entry rather than
 * a refetch that briefly shows the wrong set.
 */
export const useSubscriptionPlans = (
  organizationId?: string,
  status: PlanCatalogueFilterName = "Active",
) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    queryKey: ["subscription-plans", tenantId, organizationId ?? null, status],
    queryFn: () => subscriptionService.listPlans(organizationId, status),
    staleTime: 30_000,
  });
};
