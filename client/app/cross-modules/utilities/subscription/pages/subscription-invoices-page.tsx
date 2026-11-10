import { useT } from "@seliseblocks/genesis-os";
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
export const DocumentRow = ({
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
  const { t } = useT("utilities.subscription.pages");

  const retry = async () => {
    try {
      await resend(document.documentId);
      toast({
        variant: "success",
        title: t("INVOICES_PAGE.RETRY_QUEUED"),
        description: t("INVOICES_PAGE.RETRY_QUEUED_DESCRIPTION", {
          number: document.documentNumber,
        }),
      });
    } catch (error) {
      toast({
        variant: "destructive",
        title: t("INVOICES_PAGE.RETRY_FAILED"),
        description:
          error instanceof Error ? error.message : t("INVOICES_PAGE.SOMETHING_WENT_WRONG"),
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
        error instanceof Error ? error.message : t("INVOICES_PAGE.DOWNLOAD_FAILED"),
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
            {t("INVOICES_PAGE.PLAN_ISSUED", {
              plan: document.planName,
              date: document.issuedAtUtc.slice(0, 10),
              period: describePeriod(document),
            })}
          </p>
          {document.originalDocumentNumber && (
            <p className="mt-1 text-xs text-muted-foreground">
              {t("INVOICES_PAGE.ADJUSTS", { number: document.originalDocumentNumber })}
            </p>
          )}
        </div>

        <div className="flex items-center gap-3">
          {/* A statement states no amount; a zero here would read as "free". */}
          {document.documentType !== "ServiceStatement" && (
            <span className="whitespace-nowrap text-lg font-semibold">
              {formatMoney(document.amounts.totalMinor, document.currencyCode)}
            </span>
          )}
          <Button variant="ghost" size="sm" onClick={() => setOpen((value) => !value)}>
            {open ? t("INVOICES_PAGE.HIDE_DETAIL") : t("INVOICES_PAGE.SHOW_DETAIL")}
          </Button>
          {/* Driven by isPdfAvailable first, deliberately -- isAbandoned answers "did every
              delivery attempt finish", not "does the PDF exist". A mail failure can abandon
              delivery with the PDF already stored, and that document must still read as
              downloadable, not as failed. Only a document with no PDF at all reaches the
              render-failure branch below. */}
          {document.isPdfAvailable ? (
            <Button
              variant="outline"
              size="sm"
              disabled={downloading}
              onClick={download}
              data-testid={`download-${document.documentNumber}`}
            >
              {downloading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <Download className="mr-2 h-4 w-4" />
              )}
              {t("INVOICES_PAGE.DOWNLOAD_PDF")}
            </Button>
          ) : document.isAbandoned ? (
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
              {t("INVOICES_PAGE.GENERATION_FAILED")}
            </Button>
          ) : (
            <Button variant="outline" size="sm" disabled data-testid={`download-${document.documentNumber}`}>
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              {t("INVOICES_PAGE.PREPARING")}
            </Button>
          )}
        </div>
      </div>

      {!document.isPdfAvailable && document.isAbandoned && (
        <p className="mt-2 flex items-center gap-1 text-sm text-muted-foreground">
          <RotateCw className="h-3.5 w-3.5" />
          {t("INVOICES_PAGE.RENDER_FAILED")}
        </p>
      )}

      {/* The PDF exists, but delivery still ended abandoned -- the failure is on the email side,
          not the document. Never a plain resend button here: an unknown publish outcome may
          already have reached the subscriber, and a self-service retry risks a duplicate invoice
          email. document_no_recipient is the one harmless case, so it says so plainly instead of
          reusing the same warning tone as the dangerous one. */}
      {document.isPdfAvailable && document.isAbandoned && (
        <p
          className="mt-2 flex items-center gap-1 text-sm text-amber-700"
          data-testid={`mail-warning-${document.documentNumber}`}
        >
          <AlertTriangle className="h-3.5 w-3.5" />
          {document.lastErrorCode === "document_no_recipient"
            ? t("INVOICES_PAGE.MAIL_NO_RECIPIENT")
            : t("INVOICES_PAGE.MAIL_UNCONFIRMED")}
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
                {t("INVOICES_PAGE.LINES")}
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
                {t("INVOICES_PAGE.SETTLED_HEADING")}
              </p>
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-xs text-muted-foreground">
                    <th className="py-1 font-normal" />
                    <th className="py-1 text-right font-normal">{t("INVOICES_PAGE.PREVIOUS_TERMS")}</th>
                    <th className="py-1 text-right font-normal">{t("INVOICES_PAGE.NEW_TERMS")}</th>
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
                    <td className="py-1">{t("INVOICES_PAGE.NET_SETTLEMENT")}</td>
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

          {document.documentType !== "ServiceStatement" && (
            <div>
              <p className="mb-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">
                {t("INVOICES_PAGE.AMOUNTS")}
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
                  {t("INVOICES_PAGE.AMOUNTS_WARNING")}
                </p>
              )}
            </div>
          )}

          <div className="grid gap-2 text-xs text-muted-foreground sm:grid-cols-2">
            <p>
              <span className="font-medium text-foreground">{t("INVOICES_PAGE.BILLED_TO")}</span>{" "}
              {document.subscriberLegalName}
            </p>
            <p>
              <span className="font-medium text-foreground">{t("INVOICES_PAGE.CONTACT")}</span>{" "}
              {document.billingContactName}
              {document.billingContactEmail ? ` · ${document.billingContactEmail}` : ""}
            </p>
            <p>
              <span className="font-medium text-foreground">{t("INVOICES_PAGE.INITIATED_BY")}</span>{" "}
              {document.initiatedByName}
            </p>
            {document.pdfContentHash && (
              <p className="truncate">
                <span className="font-medium text-foreground">{t("INVOICES_PAGE.PDF_HASH")}</span>{" "}
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
  const { t } = useT("utilities.subscription.pages");
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
        title={t("INVOICES_PAGE.TITLE")}
        description={t("INVOICES_PAGE.DESCRIPTION")}
        backTo={subscriptionLink("plans")}
        icon={<FileText className="h-6 w-6" />}
      />

      <Card className="grid gap-4 p-4 sm:grid-cols-4">
        <div className="flex flex-col gap-2">
          <Label>{t("INVOICES_PAGE.DOCUMENT_TYPE")}</Label>
          <Select
            value={documentType}
            onValueChange={(value) => changeFilter(() => setDocumentType(value))}
          >
            <SelectTrigger aria-label={t("INVOICES_PAGE.DOCUMENT_TYPE")}>
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
          <Label>{t("common:STATUS")}</Label>
          <Select
            value={status}
            onValueChange={(value) => changeFilter(() => setStatus(value))}
          >
            <SelectTrigger aria-label={t("common:STATUS")}>
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
          <Label htmlFor="issuedFrom">{t("INVOICES_PAGE.ISSUED_FROM")}</Label>
          <Input
            id="issuedFrom"
            type="date"
            value={issuedFrom}
            onChange={(event) => changeFilter(() => setIssuedFrom(event.target.value))}
          />
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="issuedTo">{t("INVOICES_PAGE.ISSUED_TO")}</Label>
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

      {isLoading && <Card className="p-6 text-sm text-muted-foreground">{t("INVOICES_PAGE.LOADING")}</Card>}

      {!isLoading && data?.items.length === 0 && (
        <Card className="p-6 text-sm text-muted-foreground" data-testid="documents-empty">
          {t("INVOICES_PAGE.EMPTY")}
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
            {t("INVOICES_PAGE.BACK_TO_NEWEST")}
          </Button>
          <Button
            variant="outline"
            size="sm"
            disabled={!data?.pageInfo.hasNextPage}
            onClick={() => setCursor(data?.pageInfo.nextCursor ?? null)}
          >
            {t("INVOICES_PAGE.OLDER")}
          </Button>
        </div>
      )}
    </div>
  );
};
