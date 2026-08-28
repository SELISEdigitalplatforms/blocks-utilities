import { useProjectStore } from "@seliseblocks/genesis-os";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type {
  FinancialDocumentQuery,
  SubscriptionFinancialDocumentPage,
} from "../models/subscription-billing.model";
import { subscriptionBillingService } from "../services/subscription-billing.service";

/**
 * Whether anything in a fetched page is still being rendered.
 *
 * The only question the poll below asks. A document that has failed every attempt
 * (`isAbandoned`) is not still being worked on — it is waiting for a person — so it does not keep
 * the poll running; a document with no PDF and no abandonment is the one case still in flight.
 */
const hasPendingDocument = (page?: SubscriptionFinancialDocumentPage): boolean =>
  (page?.items ?? []).some((document) => !document.isPdfAvailable && !document.isAbandoned);

export const useFinancialDocuments = (query: FinancialDocumentQuery) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    // The whole query is in the key, cursor included, so paging forward and back is served from
    // cache rather than refetched — and changing a filter cannot show the previous filter's page.
    queryKey: ["subscription-financial-documents", tenantId, query],
    queryFn: () => subscriptionBillingService.listDocuments(query),
    staleTime: 30_000,
    // Rendering happens seconds after issuing, off-request, on a worker this page has no other way
    // to hear from. Polling only while something on the visible page is actually still pending keeps
    // an idle invoice list from polling forever, and stops the moment the last visible item resolves
    // one way or the other — generated, or abandoned.
    refetchInterval: (query) =>
      hasPendingDocument(query.state.data as SubscriptionFinancialDocumentPage | undefined)
        ? 4_000
        : false,
  });
};

/**
 * Queues a document for another delivery attempt.
 *
 * The operator recovery path for a document the render pipeline gave up on — console only, the
 * server enforces it and refuses anybody else. Invalidates the list so the retried document's row
 * updates and the poll above picks it back up.
 */
export const useResendFinancialDocument = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (documentId: string) => subscriptionBillingService.resendDocument(documentId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["subscription-financial-documents"] });
    },
  });
};

/**
 * Downloads one document's PDF and hands it to the browser.
 *
 * Fetched through the authenticated client and then handed over as a blob, rather than navigating to
 * the URL: the endpoint is behind the caller's own authorization and a plain link would arrive
 * without it. The object URL is revoked immediately — it is a handle to bytes already in memory, and
 * leaving it alive keeps the whole PDF alive with it.
 */
export const downloadFinancialDocumentPdf = async (
  documentId: string,
  documentNumber: string,
  organizationId?: string,
): Promise<void> => {
  const blob = await subscriptionBillingService.downloadDocumentPdf(
    documentId,
    organizationId,
  );

  const href = URL.createObjectURL(blob);

  try {
    const link = document.createElement("a");
    link.href = href;
    link.download = `${documentNumber}.pdf`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  } finally {
    URL.revokeObjectURL(href);
  }
};
