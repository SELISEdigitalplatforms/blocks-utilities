import { useProjectStore } from "@seliseblocks/genesis-os";
import { useQuery } from "@tanstack/react-query";
import type { FinancialDocumentQuery } from "../models/subscription-billing.model";
import { subscriptionBillingService } from "../services/subscription-billing.service";

export const useFinancialDocuments = (query: FinancialDocumentQuery) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";

  return useQuery({
    // The whole query is in the key, cursor included, so paging forward and back is served from
    // cache rather than refetched — and changing a filter cannot show the previous filter's page.
    queryKey: ["subscription-financial-documents", tenantId, query],
    queryFn: () => subscriptionBillingService.listDocuments(query),
    staleTime: 30_000,
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
