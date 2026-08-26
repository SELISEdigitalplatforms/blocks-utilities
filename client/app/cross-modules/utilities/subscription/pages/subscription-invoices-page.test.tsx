import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { SubscriptionFinancialDocument } from "../models/subscription-billing.model";

const { useFinancialDocuments, downloadFinancialDocumentPdf } = vi.hoisted(() => ({
  useFinancialDocuments: vi.fn(),
  downloadFinancialDocumentPdf: vi.fn(),
}));

vi.mock("../hooks/use-financial-documents", () => ({
  useFinancialDocuments,
  downloadFinancialDocumentPdf,
}));

vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => ({
    data: { organizations: [{ itemId: "org-1", name: "Northwind" }] },
    isError: false,
  }),
}));

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));

import { SubscriptionInvoicesPage } from "./subscription-invoices-page";

/**
 * Currency formatting puts a non-breaking space between the code and the number.
 */
const plain = (value: string) => value.replace(/\u00a0/g, " ");

const document = (
  overrides: Partial<SubscriptionFinancialDocument> = {},
): SubscriptionFinancialDocument => ({
  documentId: "doc-1",
  documentNumber: "INV-2026-000001",
  documentType: "Invoice",
  status: "Issued",
  issuedAtUtc: "2026-08-25T10:00:00Z",
  subscriptionId: "sub-1",
  currencyCode: "CHF",
  planCode: "pro",
  planName: "Pro",
  periodStartUtc: "2026-01-01T00:00:00Z",
  periodEndUtc: "2027-01-01T00:00:00Z",
  periodLocalStart: "2026-01-01",
  periodLocalEnd: "2027-01-01",
  timeZoneId: "Europe/Zurich",
  amounts: {
    grossSubtotalMinor: 100_000,
    automaticDiscountMinor: 8_000,
    quantityDiscountMinor: 0,
    promotionalDiscountMinor: 2_000,
    netSubtotalMinor: 90_000,
    taxRateBasisPoints: 770,
    taxMode: "Exclusive",
    taxAmountMinor: 6_930,
    creditAppliedMinor: 1_000,
    totalMinor: 95_930,
    automaticDiscountBasisPoints: 800,
    quantityDiscountBasisPoints: null,
    discountCombination: "BestDiscount",
    promotionCode: "launch20",
  },
  settlement: null,
  lines: [
    {
      description: "Pro",
      quantity: 1,
      unitAmountMinor: 100_000,
      amountMinor: 100_000,
      itemKey: null,
    },
  ],
  trial: null,
  subscriberLegalName: "Northwind Trading AG",
  billingContactName: "Ada Byron",
  billingContactEmail: "ada@northwind.example",
  initiatedByName: "System renewal",
  initiatedByUserId: null,
  paymentDetailId: "pay-1",
  refundId: null,
  originalDocumentId: null,
  originalDocumentNumber: null,
  isPdfAvailable: true,
  pdfContentHash: "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
  downloadUrl: "/api/subscriptions/invoices/doc-1/pdf",
  ...overrides,
});

const listing = (items: SubscriptionFinancialDocument[], hasNextPage = false) => ({
  data: {
    items,
    pageInfo: { pageSize: 25, hasNextPage, nextCursor: hasNextPage ? "cursor-2" : null },
  },
  isLoading: false,
  error: null,
});

const renderPage = () =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <SubscriptionInvoicesPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );

describe("subscription invoices page", () => {
  it("lists a document with its number, type and total", () => {
    useFinancialDocuments.mockReturnValue(listing([document()]));

    renderPage();

    expect(screen.getByText("INV-2026-000001")).toBeInTheDocument();
    expect(screen.getByText("Invoice")).toBeInTheDocument();
    expect(plain(screen.getByText(/959\.30/).textContent ?? "")).toContain("959.30");
  });

  it("shows each discount source separately once the detail is opened", async () => {
    useFinancialDocuments.mockReturnValue(listing([document()]));

    renderPage();
    await userEvent.click(screen.getByRole("button", { name: "Show detail" }));

    // The whole reason the ledger stores them apart. One "discount" line cannot say which promise
    // the subscriber was actually kept.
    expect(screen.getByText("Automatic price discount (8%)")).toBeInTheDocument();
    expect(screen.getByText("Promotional discount (launch20)")).toBeInTheDocument();
    expect(screen.getByText("Tax (7.7%, added)")).toBeInTheDocument();
    expect(screen.getByText("Account credit applied")).toBeInTheDocument();
  });

  it("shows the parties as the document states them, not as they stand now", async () => {
    useFinancialDocuments.mockReturnValue(listing([document()]));

    renderPage();
    await userEvent.click(screen.getByRole("button", { name: "Show detail" }));

    // Otherwise the page and the PDF disagree the moment somebody edits the profile.
    expect(screen.getByText(/Northwind Trading AG/)).toBeInTheDocument();
    expect(screen.getByText(/System renewal/)).toBeInTheDocument();
  });

  it("shows both sides of a settlement rather than one number", async () => {
    useFinancialDocuments.mockReturnValue(
      listing([
        document({
          documentNumber: "INV-2026-000002",
          settlement: {
            outgoing: {
              grossAmountMinor: 10_000,
              builtInDiscountMinor: 0,
              promotionalDiscountMinor: 0,
              taxAmountMinor: 770,
              periodTotalMinor: 10_770,
              proratedValueMinor: 3_000,
            },
            target: {
              grossAmountMinor: 30_000,
              builtInDiscountMinor: 0,
              promotionalDiscountMinor: 0,
              taxAmountMinor: 2_310,
              periodTotalMinor: 32_310,
              proratedValueMinor: 9_000,
            },
            creditConsumedMinor: 0,
            netSettlementMinor: 6_000,
          },
        }),
      ]),
    );

    renderPage();
    await userEvent.click(screen.getByRole("button", { name: "Show detail" }));

    expect(screen.getByText("Previous terms")).toBeInTheDocument();
    expect(screen.getByText("New terms")).toBeInTheDocument();
    expect(screen.getByText("Net settlement")).toBeInTheDocument();
  });

  it("states a trial invoice's terms and that nothing was charged", async () => {
    useFinancialDocuments.mockReturnValue(
      listing([
        document({
          documentNumber: "INV-2026-000003",
          documentType: "TrialInvoice",
          trial: {
            startsAtUtc: "2026-08-01T00:00:00Z",
            endsAtUtc: "2026-08-15T00:00:00Z",
            requiresPaymentMethod: false,
            firstBillingAtUtc: "2026-08-15T00:00:00Z",
          },
        }),
      ]),
    );

    renderPage();

    expect(screen.getByText("Trial invoice")).toBeInTheDocument();
    expect(screen.getByText("No payment due")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Show detail" }));
    expect(screen.getByText(/no payment method was required/)).toBeInTheDocument();
  });

  it("names the invoice a credit note adjusts", () => {
    useFinancialDocuments.mockReturnValue(
      listing([
        document({
          documentNumber: "CRN-2026-000001",
          documentType: "CreditNote",
          originalDocumentNumber: "INV-2026-000001",
        }),
      ]),
    );

    renderPage();

    // A credit note on its own is meaningless, and this is the first thing anybody reconciling it
    // looks for.
    expect(screen.getByText("Credit note")).toBeInTheDocument();
    expect(screen.getByText("Adjusts invoice INV-2026-000001")).toBeInTheDocument();
  });

  it("downloads through the authenticated client rather than following a link", async () => {
    useFinancialDocuments.mockReturnValue(listing([document()]));
    downloadFinancialDocumentPdf.mockResolvedValue(undefined);

    renderPage();
    await userEvent.click(screen.getByTestId("download-INV-2026-000001"));

    // A plain anchor would arrive without the caller's authorization, and the endpoint is behind it.
    await waitFor(() =>
      expect(downloadFinancialDocumentPdf).toHaveBeenCalledWith(
        "doc-1",
        "INV-2026-000001",
        // No organization in the URL, so none is forwarded: an application caller is scoped by
        // their token and only the console may name one.
        undefined,
      ),
    );
  });

  it("reports a failed download instead of failing silently", async () => {
    useFinancialDocuments.mockReturnValue(listing([document()]));
    downloadFinancialDocumentPdf.mockRejectedValue(new Error("storage is unreachable"));

    renderPage();
    await userEvent.click(screen.getByTestId("download-INV-2026-000001"));

    expect(await screen.findByTestId("download-error")).toHaveTextContent(
      "storage is unreachable",
    );
  });

  it("disables the download while the pdf is still being rendered", () => {
    useFinancialDocuments.mockReturnValue(
      listing([document({ isPdfAvailable: false, pdfContentHash: null })]),
    );

    renderPage();

    // Read from the flag rather than by probing for a 404: documents are rendered asynchronously, so
    // an invoice exists for a few seconds before its PDF does.
    expect(screen.getByTestId("download-INV-2026-000001")).toBeDisabled();
    expect(screen.getByText("Preparing…")).toBeInTheDocument();
  });

  it("warns when a document's own figures do not add up", async () => {
    useFinancialDocuments.mockReturnValue(
      listing([
        document({
          amounts: {
            ...document().amounts,
            automaticDiscountMinor: 8_000,
            promotionalDiscountMinor: 0,
            netSubtotalMinor: 100_000,
          },
        }),
      ]),
    );

    renderPage();
    await userEvent.click(screen.getByRole("button", { name: "Show detail" }));

    // A pre-breakdown charge. Told rather than hidden, because the alternative is the subscriber
    // reaching for a calculator and concluding we cannot add up.
    expect(screen.getByTestId("amounts-warning")).toBeInTheDocument();
  });

  it("says plainly when there is nothing to show yet", () => {
    useFinancialDocuments.mockReturnValue(listing([]));

    renderPage();

    expect(screen.getByTestId("documents-empty")).toBeInTheDocument();
  });

  it("resets paging when a filter changes", async () => {
    useFinancialDocuments.mockReturnValue(listing([document()], true));

    renderPage();
    await userEvent.click(screen.getByRole("button", { name: "Older" }));

    await waitFor(() =>
      expect(useFinancialDocuments).toHaveBeenLastCalledWith(
        expect.objectContaining({ after: "cursor-2" }),
      ),
    );

    await userEvent.click(screen.getByRole("combobox", { name: "Document type" }));
    await userEvent.click(screen.getByRole("option", { name: "Credit notes" }));

    // The cursor encodes a position in the previous result set. Carried over, it would page through
    // documents the new filter does not select.
    await waitFor(() =>
      expect(useFinancialDocuments).toHaveBeenLastCalledWith(
        expect.objectContaining({ after: null, documentType: "CreditNote" }),
      ),
    );
  });

  it("sends a date filter as instants covering the whole of the closing day", async () => {
    useFinancialDocuments.mockReturnValue(listing([document()]));

    renderPage();
    await userEvent.type(screen.getByLabelText("Issued to"), "2026-12-31");

    // "To 31 December" has to include documents issued that afternoon, or a tax-year query silently
    // drops its last day.
    await waitFor(() =>
      expect(useFinancialDocuments).toHaveBeenLastCalledWith(
        expect.objectContaining({ issuedToUtc: "2026-12-31T23:59:59Z" }),
      ),
    );
  });
});
