import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type {
  CreditPurchaseRequest,
  GrantCreditsRequest,
  MoveCreditsRequest,
} from "../models/subscription-simulation.model";
import { subscriptionCreditsService } from "../services/subscription-credits.service";

export const CREDIT_GRANTS_QUERY_KEY = "subscription-credit-grants";

export const useCreditGrants = (subscriptionId: string | undefined, organizationId?: string) =>
  useQuery({
    queryKey: [CREDIT_GRANTS_QUERY_KEY, subscriptionId, organizationId],
    queryFn: () => subscriptionCreditsService.listGrants(subscriptionId!, organizationId),
    enabled: Boolean(subscriptionId),
  });

/**
 * Anything that adds or moves credits changes the grant list, what usage shows as remaining, and
 * — after a purchase — the invoice list, so all three are refreshed.
 */
const useCreditMutation = <TRequest, TResult>(
  mutationFn: (request: TRequest) => Promise<TResult>,
) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [CREDIT_GRANTS_QUERY_KEY] });
      queryClient.invalidateQueries({ queryKey: ["subscription-usage"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-simulation-entitlements"] });
    },
  });
};

export const usePreviewCreditPurchase = (subscriptionId: string) =>
  useMutation({
    mutationFn: (request: CreditPurchaseRequest) =>
      subscriptionCreditsService.previewPurchase(subscriptionId, request),
  });

export const usePurchaseCredits = (subscriptionId: string) =>
  useCreditMutation((request: CreditPurchaseRequest) =>
    subscriptionCreditsService.purchase(subscriptionId, request),
  );

export const useGrantCredits = (subscriptionId: string) =>
  useCreditMutation((request: GrantCreditsRequest) =>
    subscriptionCreditsService.grant(subscriptionId, request),
  );

export const useMoveCredits = (subscriptionId: string) =>
  useCreditMutation((request: MoveCreditsRequest) =>
    subscriptionCreditsService.move(subscriptionId, request),
  );
