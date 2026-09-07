import { test } from "../../support/test-base";
import {
  openInvoices,
  verifyPageHeaderAndFilterControlsVisible,
  verifyEmptyStateShowsNoDocumentsMatchFiltersYet,
  verifyDocumentTypeFilterNarrowsListing,
  verifyStatusFilterNarrowsListing,
  verifyIssuedFromAndToComposeDateRange,
  verifyEmptyDateRangeShowsEmptyState,
  verifyShowDetailExpandsDocumentRow,
  verifyBackToPlansLinkReturnsToPlans,
} from "../../page/subscripptions/invoices";

test.describe("flow: Subscriptions - Invoices", () => {
  test("Invoices & credit notes - header, filters (type/status/dates), empty state, row expansion", async ({
    page,
  }) => {
    test.setTimeout(180_000);

    await openInvoices(page);

    await test.step("[Positive] page header and the four filter controls are visible", () =>
      verifyPageHeaderAndFilterControlsVisible(page));
    await test.step("[Positive] empty environment shows the empty-state copy", () =>
      verifyEmptyStateShowsNoDocumentsMatchFiltersYet(page));
    await test.step("[Positive] Document type filter narrows the listing", () =>
      verifyDocumentTypeFilterNarrowsListing(page));
    await test.step("[Positive] Status filter narrows the listing", () =>
      verifyStatusFilterNarrowsListing(page));
    await test.step("[Positive] Issued-from and Date filter compose into a date range", () =>
      verifyIssuedFromAndToComposeDateRange(page));
    await test.step("[Negative] a date range with no documents lands on the empty state", () =>
      verifyEmptyDateRangeShowsEmptyState(page));
    await test.step("[Positive] Show detail expands a document row and surfaces its figures", () =>
      verifyShowDetailExpandsDocumentRow(page));
    await test.step("[Positive] 'Back to plans' returns to the plans list", () =>
      verifyBackToPlansLinkReturnsToPlans(page));
  });
});
