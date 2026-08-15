import { useQuery } from "@tanstack/react-query";
import { subscriptionService } from "../services/subscription.service";

export const useSubscriptionPlan = (
  planId: string | undefined,
  organizationId?: string,
) =>
  useQuery({
    queryKey: ["subscription-plan", planId, organizationId ?? null],
    queryFn: () => subscriptionService.getPlan(planId!, organizationId),
    enabled: Boolean(planId),
  });
