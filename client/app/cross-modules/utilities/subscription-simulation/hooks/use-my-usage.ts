import { useProjectStore } from "@seliseblocks/genesis-os";
import { useQuery } from "@tanstack/react-query";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

/**
 * The caller's own balances, from `GET /api/subscription-usage/mine`.
 *
 * Same response shape as {@link useCurrentUsage}, so it must not share its key: the two reads
 * would overwrite each other in the cache and the toggle would show whichever landed last. Still
 * under "subscription-usage", so every invalidation that refreshes the organization's figures —
 * recording, assigning, a quantity change — refreshes these too.
 */
export const useMyUsage = (organizationId?: string, enabled = true) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    queryKey: ["subscription-usage", tenantId, organizationId ?? null, "mine"],
    queryFn: () => subscriptionSimulationService.getMyUsage(organizationId),
    staleTime: 5_000,
    enabled,
  });
};
