import { useProjectStore } from "@seliseblocks/genesis-os";
import { useQuery } from "@tanstack/react-query";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

/** The once-per-session summary read, for the entitlements list. */
export const useEntitlements = (organizationId?: string) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    queryKey: ["subscription-simulation-entitlements", tenantId, organizationId ?? null],
    queryFn: () => subscriptionSimulationService.getEntitlements(organizationId),
    staleTime: 5_000,
  });
};
