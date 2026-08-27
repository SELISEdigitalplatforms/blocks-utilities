import { useState } from "react";
import { AlertTriangle, Download, FileText, Loader2, RotateCw } from "lucide-react";
import { toast } from "@/hooks/use-toast";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { SubscriptionPlanPageHeader } from "../components/subscription-plan-page-header";
import {
  ANY_DOCUMENT_FILTER,
  FINANCIAL_DOCUMENT_PAGE_SIZE,
  FINANCIAL_DOCUMENT_STATUS_OPTIONS,
  FINANCIAL_DOCUMENT_TYPE_OPTIONS,
} from "../constants/subscription.constants";
import {
  downloadFinancialDocumentPdf,
  useFinancialDocuments,
  useResendFinancialDocument,
} from "../hooks/use-financial-documents";
import { useOrganizationScope } from "../hooks/use-organization-scope";
import { useSubscriptionLink } from "../hooks/use-subscription-link";
import type {
  FinancialDocumentStatus,
  FinancialDocumentType,
  SubscriptionFinancialDocument,
} from "../models/subscription-billing.model";
import { formatMoney } from "../utilities/subscription-format";
import {
  amountsReconcile,
  describeDocumentStatus,
  describeDocumentType,
  describePeriod,
  describeTrial,
  documentAmountRows,
  settlementRows,
} from "../utilities/financial-document-format";

const badgeVariant = (document: SubscriptionFinancialDocument) => {
  if (document.documentType === "CreditNote") {
    return "secondary" as const;
  }

  return document.status === "Issued" ? ("default" as const) : ("outline" as const);
};

/**
 * One document, expandable into its own figures.
 *
 * Collapsed by default because a list is for finding an invoice, not for reading one; expanded it
 * shows every discount source separately, the settlement's two sides where there is one, and the
 * parties as the document itself states them rather than as the profile stands now.
 */
const DocumentRow = ({
  document,
  organizationId,
}: {
  document: SubscriptionFinancialDocument;
  organizationId?: string;
}) => {
  const [open, setOpen] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState<string | null>(null);
  const { mutateAsync: resend, isPending: resending } = useResendFinancialDocument();

  const retry = async () => {
    try {
      await resend(document.documentId);
      toast({
        variant: "success",
        title: "Queued for another attempt",
        description: `${document.documentNumber} will be rendered again.`,
      });
    } catch (error) {
      toast({
        variant: "destructive",
        title: "Could not queue a retry",
        description: error instanceof Error ? error.message : "Something went wrong.",
      });
    }
  };

  const download = async () => {
    setDownloadError(null);
    setDownloading(true);

    try {
      await downloadFinancialDocumentPdf(
        document.documentId,
        document.documentNumber,
        organizationId,
      );
    } catch (error) {
      setDownloadError(
        error instanceof Error ? error.message : "The PDF could not be downloaded.",
      );
    } finally {
      setDownloading(false);
    }
  };

  return (
    <Card className="p-4" data-testid={`document-${document.documentNumber}`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-medium">{document.documentNumber}</span>
            <Badge variant={badgeVariant(document)}>
              {describeDocumentType(document.documentType)}
            </Badge>
            <span className="text-xs text-muted-foreground">
              {describeDocumentStatus(document)}
            </span>
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            {document.planName} · issued {document.issuedAtUtc.slice(0, 10)} ·{" "}
            {describePeriod(document)}
          </p>
          {document.originalDocumentNumber && (
            <p className="mt-1 text-xs text-muted-foreground">
              Adjusts invoice {document.originalDocumentNumber}
            </p>
          )}
        </div>

        <div className="flex items-center gap-3">
          <span className="whitespace-nowrap text-lg font-semibold">
            {formatMoney(document.amounts.totalMinor, document.currencyCode)}
          </span>
          <Button variant="ghost" size="sm" onClick={() => setOpen((value) => !value)}>
            {open ? "Hide detail" : "Show detail"}
          </Button>
          {/* Rendered from isPdfAvailable/isAbandoned rather than by probing each row for a 404:
              documents are rendered asynchronously, so an invoice can exist for a few seconds
              before its PDF does, and every attempt can also run out and need a person. */}
          {document.isAbandoned ? (
            <Button
              variant="outline"
              size="sm"
              disabled={resending}
              onClick={retry}
              data-testid={`retry-${document.documentNumber}`}
            >
              {resending ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <AlertTriangle className="mr-2 h-4 w-4 text-destructive" />
              )}
              Generation failed
            </Button>
          ) : (
            <Button
              variant="outline"
              size="sm"
              disabled={!document.isPdfAvailable || downloading}
              onClick={download}
              data-testid={`download-${document.documentNumber}`}
            >
              {downloading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <Download className="mr-2 h-4 w-4" />
              )}
              {document.isPdfAvailable ? "Download PDF" : "Preparing…"}
            </Button>
          )}
        </div>
      </div>

      {document.isAbandoned && (
        <p className="mt-2 flex items-center gap-1 text-sm text-muted-foreground">
          <RotateCw className="h-3.5 w-3.5" />
          Rendering did not succeed after every attempt. Retry once the underlying issue is fixed.
        </p>
      )}

      {downloadError && (
        <p className="mt-2 text-sm text-destructive" data-testid="download-error">
          {downloadError}
        </p>
      )}

      {open && (
        <div className="mt-4 flex flex-col gap-4 border-t pt-4">
          {document.trial && (
            <p className="rounded-md bg-muted/50 p-3 text-sm">{describeTrial(document.trial)}</p>
          )}

          {document.lines.length > 0 && (
            <div>
              <p className="mb-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">
                Lines
              </p>
              <ul className="flex flex-col gap-1 text-sm">
                {document.lines.map((line, index) => (
                  <li
                    key={`${line.itemKey ?? line.description}-${index}`}
                    className="flex justify-between gap-4"
                  >
                    <span className="text-muted-foreground">
                      {line.description}
                      {line.quantity != null && ` × ${line.quantity.toLocaleString()}`}
                    </span>
                    <span>{formatMoney(line.amountMinor, document.currencyCode)}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {document.settlement && (
            <div>
              <p className="mb-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">
                How this change was settled
              </p>
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-xs text-muted-foreground">
                    <th className="py-1 font-normal" />
                    <th className="py-1 text-right font-normal">Previous terms</th>
                    <th className="py-1 text-right font-normal">New terms</th>
                  </tr>
                </thead>
                <tbody>
                  {settlementRows(document.settlement, document.currencyCode).map((row) => (
                    <tr key={row.label}>
                      <td className="py-1 text-muted-foreground">{row.label}</td>
                      <td className="py-1 text-right">{row.outgoing}</td>
                      <td className="py-1 text-right">{row.target}</td>
                    </tr>
                  ))}
                  <tr className="border-t font-medium">
                    <td className="py-1">Net settlement</td>
                    <td />
                    <td className="py-1 text-right">
                      {formatMoney(
                        document.settlement.netSettlementMinor,
                        document.currencyCode,
                      )}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          )}

          <div>
            <p className="mb-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">
              Amounts
            </p>
            <table className="w-full max-w-sm text-sm">
              <tbody>
                {documentAmountRows(document.amounts, document.currencyCode).map((row) => (
                  <tr key={row.label} className={row.isTotal ? "border-t font-medium" : undefined}>
                    <td className="py-1 pr-6 text-muted-foreground">{row.label}</td>
                    <td className="py-1 text-right">{row.amount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {!amountsReconcile(document.amounts) && (
              <p className="mt-2 text-xs text-amber-700" data-testid="amounts-warning">
                These figures do not add up on their own. That happens on charges taken before the
                full breakdown was recorded; the total is what was actually charged.
              </p>
            )}
          </div>

          <div className="grid gap-2 text-xs text-muted-foreground sm:grid-cols-2">
            <p>
              <span className="font-medium text-foreground">Billed to:</span>{" "}
              {document.subscriberLegalName}
            </p>
            <p>
              <span className="font-medium text-foreground">Contact:</span>{" "}
              {document.billingContactName}
              {document.billingContactEmail ? ` · ${document.billingContactEmail}` : ""}
            </p>
            <p>
              <span className="font-medium text-foreground">Initiated by:</span>{" "}
              {document.initiatedByName}
            </p>
            {document.pdfContentHash && (
              <p className="truncate">
                <span className="font-medium text-foreground">PDF SHA-256:</span>{" "}
                {document.pdfContentHash.slice(0, 16)}…
              </p>
            )}
          </div>
        </div>
      )}
    </Card>
  );
};

/**
 * The organization's invoices, trial invoices and credit notes.
 *
 * Answered from the application's own document ledger, so a trial and a refund appear here as well
 * as a charge — the payment-derived list this replaced could only show things that had a payment.
 */
export const SubscriptionInvoicesPage = () => {
  const organizationId = useOrganizationScope();
  const subscriptionLink = useSubscriptionLink();
  const [documentType, setDocumentType] = useState<string>(ANY_DOCUMENT_FILTER);
  const [status, setStatus] = useState<string>(ANY_DOCUMENT_FILTER);
  const [issuedFrom, setIssuedFrom] = useState("");
  const [issuedTo, setIssuedTo] = useState("");
  const [cursor, setCursor] = useState<string | null>(null);

  const { data, isLoading, error } = useFinancialDocuments({
    pageSize: FINANCIAL_DOCUMENT_PAGE_SIZE,
    after: cursor,
    documentType:
      documentType === ANY_DOCUMENT_FILTER
        ? null
        : (documentType as FinancialDocumentType),
    status: status === ANY_DOCUMENT_FILTER ? null : (status as FinancialDocumentStatus),
    // Sent as instants because the server compares against issue timestamps. The end of the range is
    // pushed to the end of its day, so "to 31 December" includes documents issued that afternoon.
    issuedFromUtc: issuedFrom ? `${issuedFrom}T00:00:00Z` : null,
    issuedToUtc: issuedTo ? `${issuedTo}T23:59:59Z` : null,
    organizationId,
  });

  // Any filter change invalidates the cursor: it encodes a position in the previous result set, and
  // carrying it over would page through documents the new filter does not select.
  const changeFilter = (apply: () => void) => {
    setCursor(null);
    apply();
  };

  return (
    <div className="flex flex-col gap-6">
      <SubscriptionPlanPageHeader
        title="Invoices & credit notes"
        description="Every document this application has issued: invoices, trial invoices and credit notes, with the figures each was issued on."
        backTo={subscriptionLink("plans")}
        icon={<FileText className="h-6 w-6" />}
      />

      <Card className="grid gap-4 p-4 sm:grid-cols-4">
        <div className="flex flex-col gap-2">
          <Label>Document type</Label>
          <Select
            value={documentType}
            onValueChange={(value) => changeFilter(() => setDocumentType(value))}
          >
            <SelectTrigger aria-label="Document type">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {FINANCIAL_DOCUMENT_TYPE_OPTIONS.map((option) => (
                <SelectItem key={option.value} value={option.value}>
                  {option.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="flex flex-col gap-2">
          <Label>Status</Label>
          <Select
            value={status}
            onValueChange={(value) => changeFilter(() => setStatus(value))}
          >
            <SelectTrigger aria-label="Status">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {FINANCIAL_DOCUMENT_STATUS_OPTIONS.map((option) => (
                <SelectItem key={option.value} value={option.value}>
                  {option.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="issuedFrom">Issued from</Label>
          <Input
            id="issuedFrom"
            type="date"
            value={issuedFrom}
            onChange={(event) => changeFilter(() => setIssuedFrom(event.target.value))}
          />
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="issuedTo">Issued to</Label>
          <Input
            id="issuedTo"
            type="date"
            value={issuedTo}
            onChange={(event) => changeFilter(() => setIssuedTo(event.target.value))}
          />
        </div>
      </Card>

      {error instanceof Error && (
        <Card className="border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
          {error.message}
        </Card>
      )}

      {isLoading && <Card className="p-6 text-sm text-muted-foreground">Loading…</Card>}

      {!isLoading && data?.items.length === 0 && (
        <Card className="p-6 text-sm text-muted-foreground" data-testid="documents-empty">
          No documents match these filters yet. A document appears within seconds of a payment
          confirming, a trial starting, or a refund being settled.
        </Card>
      )}

      <div className="flex flex-col gap-3">
        {data?.items.map((document) => (
          <DocumentRow
            key={document.documentId}
            document={document}
            organizationId={organizationId}
          />
        ))}
      </div>

      {(cursor || data?.pageInfo.hasNextPage) && (
        <div className="flex items-center justify-between">
          <Button
            variant="outline"
            size="sm"
            disabled={!cursor}
            onClick={() => setCursor(null)}
          >
            Back to newest
          </Button>
          <Button
            variant="outline"
            size="sm"
            disabled={!data?.pageInfo.hasNextPage}
            onClick={() => setCursor(data?.pageInfo.nextCursor ?? null)}
          >
            Older
          </Button>
        </div>
      )}
    </div>
  );
};
