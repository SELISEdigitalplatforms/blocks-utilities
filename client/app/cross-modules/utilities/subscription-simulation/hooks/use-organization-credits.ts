import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type {
  OrganizationCreditPurchaseRequest,
  OrganizationGrantCreditsRequest,
} from "../models/subscription-simulation.model";
import { organizationCreditsService } from "../services/organization-credits.service";

export const ORGANIZATION_CREDITS_QUERY_KEY = "organization-credits";

export const useOrganizationCreditGrants = (organizationId?: string) =>
  useQuery({
    queryKey: [ORGANIZATION_CREDITS_QUERY_KEY, "grants", organizationId ?? null],
    queryFn: () => organizationCreditsService.listGrants(organizationId),
  });

export const useOrganizationCreditPurchases = (organizationId?: string) =>
  useQuery({
    queryKey: [ORGANIZATION_CREDITS_QUERY_KEY, "purchases", organizationId ?? null],
    queryFn: () => organizationCreditsService.listPurchases(organizationId),
  });

export const useOrganizationCreditInvoices = (organizationId?: string) =>
  useQuery({
    queryKey: [ORGANIZATION_CREDITS_QUERY_KEY, "invoices", organizationId ?? null],
    queryFn: () => organizationCreditsService.listInvoices(organizationId),
  });

/** A purchase or a grant changes the grant list, the purchase list, the invoices and what usage shows as remaining. */
const useOrganizationCreditMutation = <TRequest, TResult>(mutationFn: (request: TRequest) => Promise<TResult>) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [ORGANIZATION_CREDITS_QUERY_KEY] });
      queryClient.invalidateQueries({ queryKey: ["subscription-usage"] });
    },
  });
};

export const usePreviewOrganizationCreditPurchase = () =>
  useMutation({
    mutationFn: (request: OrganizationCreditPurchaseRequest) => organizationCreditsService.previewPurchase(request),
  });

export const usePurchaseOrganizationCredits = () =>
  useOrganizationCreditMutation((request: OrganizationCreditPurchaseRequest) =>
    organizationCreditsService.purchase(request),
  );

export const useGrantOrganizationCredits = () =>
  useOrganizationCreditMutation((request: OrganizationGrantCreditsRequest) =>
    organizationCreditsService.grant(request),
  );
