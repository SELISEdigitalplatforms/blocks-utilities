import { useProjectStore } from "@seliseblocks/genesis-os";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { subscriptionSimulationService } from "../services/subscription-simulation.service";

/**
 * The organization's user-wise subscriptions.
 *
 * Keyed under "subscription-simulation-current" so every path that already refreshes the current
 * subscription — subscribing, paying, cancelling, changing quantity — refreshes this list too.
 */
export const useMemberBasedSubscriptions = (organizationId?: string) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    queryKey: ["subscription-simulation-current", "member-based", tenantId, organizationId ?? null],
    queryFn: () => subscriptionSimulationService.listMemberBasedSubscriptions(organizationId),
    staleTime: 5_000,
  });
};

export const useMembers = (subscriptionId: string) =>
  useQuery({
    queryKey: ["subscription-simulation-members", subscriptionId],
    queryFn: () => subscriptionSimulationService.listMembers(subscriptionId),
    staleTime: 5_000,
  });

/**
 * Assignment changes what the people involved may spend, so usage and entitlements are re-read
 * with the members — a stale balance beside a fresh roster would contradict it.
 */
const useInvalidateAfterMembership = () => {
  const queryClient = useQueryClient();

  return (subscriptionId: string) => {
    queryClient.invalidateQueries({ queryKey: ["subscription-simulation-members", subscriptionId] });
    queryClient.invalidateQueries({ queryKey: ["subscription-simulation-current"] });
    queryClient.invalidateQueries({ queryKey: ["subscription-simulation-entitlements"] });
    queryClient.invalidateQueries({ queryKey: ["subscription-usage"] });
  };
};

export const useAssignMembers = () => {
  const invalidate = useInvalidateAfterMembership();

  return useMutation({
    mutationFn: ({ subscriptionId, userIds }: { subscriptionId: string; userIds: string[] }) =>
      subscriptionSimulationService.assignMembers(subscriptionId, { userIds }),
    onSuccess: (_result, { subscriptionId }) => invalidate(subscriptionId),
  });
};

export const useReleaseMember = () => {
  const invalidate = useInvalidateAfterMembership();

  return useMutation({
    mutationFn: ({ subscriptionId, userId }: { subscriptionId: string; userId: string }) =>
      subscriptionSimulationService.releaseMember(subscriptionId, userId),
    onSuccess: (_result, { subscriptionId }) => invalidate(subscriptionId),
  });
};
