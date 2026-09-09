import { useProjectStore } from "@seliseblocks/genesis-os";
import { useQuery } from "@tanstack/react-query";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

/**
 * Every meter's current allowance, read from the counters.
 *
 * Keyed under "subscription-usage" so the invalidations the seat-count paths already fire
 * (use-people, use-user) refresh this too — a quantity change moves the included allowance.
 */
export const useCurrentUsage = (organizationId?: string) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    queryKey: ["subscription-usage", tenantId, organizationId ?? null],
    queryFn: () => subscriptionSimulationService.getCurrentUsage(organizationId),
    staleTime: 5_000,
  });
};
