import { useProjectStore } from "@seliseblocks/genesis-os";
import { useQuery } from "@tanstack/react-query";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

export const useCurrentSimulatedSubscription = (organizationId?: string) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    queryKey: ["subscription-simulation-current", tenantId, organizationId ?? null],
    queryFn: () => subscriptionSimulationService.getCurrentSubscription(organizationId),
    staleTime: 5_000,
  });
};
