import { useQuery } from "@tanstack/react-query";
import { AUDIT_TRAIL_DEFAULT_LIMIT } from "../constants/subscription-simulation.constants";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

/** `enabled` keeps this from fetching until the trail is actually opened. */
export const useAuditTrail = (
  subscriptionId: string | undefined,
  organizationId: string | undefined,
  limit: number = AUDIT_TRAIL_DEFAULT_LIMIT,
  enabled = true,
) =>
  useQuery({
    queryKey: ["subscription-simulation-audit-trail", subscriptionId, organizationId ?? null, limit],
    queryFn: () =>
      subscriptionSimulationService.getAuditTrail(subscriptionId!, organizationId, limit),
    enabled: enabled && Boolean(subscriptionId),
  });
