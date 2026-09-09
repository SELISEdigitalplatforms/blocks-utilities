import { test } from "../../support/test-base";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";
import {
  emptyPaymentsListResponder,
  stubPaymentsListEndpoint,
  stubPaymentsListOrganizationsEndpoint,
  openPaymentList,
  verifyListHeaderAndFilters,
  verifyFilterFieldsPresent,
  verifyApplyFiltersCanBeTriggered,
  verifyEmptyEnvironmentShowsNoPaymentsYet,
  verifyProviderFilterMultiSelect,
  verifyProviderFilterTwoSelected,
  verifyProviderFilterCustomName,
  verifyStatusFilterMultiSelect,
  verifyCurrencySingleSelect,
  verifyPaymentFlowSingleSelect,
  verifyMoreFiltersExpandCollapse,
  verifyMoreFiltersLetsUserType,
  verifyAmountRangeValidation,
  verifyDateRangeValidation,
  verifyResetFiltersClearsAll,
  verifyRefreshButtonRefetches,
  verifyOrganizationFilter,
  verifyRowsPerPageSelectorChanges,
  verifyNoMatchingPaymentsShowsFilteredEmptyState,
  verifyLastRefreshedTimestamp,
  verifyPaymentTableRendersRows,
  verifySortByColumnTogglesDirection,
  verifyPaginationEnablesNext,
  verifyErrorStateSurfacesTryAgain,
  verifyRefundActionOpensDialog,
  type PaymentsListHolder,
} from "../../page/payments/payment-list";

test.describe("flow: Payments — Payment List", () => {
  test("Payment list — header, filters (provider/status/currency/flow/org/more), rows, sort, pagination, errors, refund", async ({
    page,
  }) => {
    test.setTimeout(240_000);

    // Route stubs — holder's responder can be swapped at any point.
    const holder: PaymentsListHolder = { responder: emptyPaymentsListResponder() };
    await stubPaymentsListEndpoint(page, holder);
    await stubPaymentsListOrganizationsEndpoint(page);

    await openUtilitiesDashboard(page);
    await openPaymentList(page);

    await test.step("[Positive] header shows the live status badge and filters", () =>
      verifyListHeaderAndFilters(page));
    await test.step("[Positive] filter fields cover provider/status/currency/flow/organization", () =>
      verifyFilterFieldsPresent(page));
    await test.step("[Positive] Apply filters can be triggered without error", () =>
      verifyApplyFiltersCanBeTriggered(page));
    await test.step("[Positive] empty environment shows 'No payments yet' with the explanatory subtitle", () =>
      verifyEmptyEnvironmentShowsNoPaymentsYet(page));
    await test.step("[Positive] Provider filter multi-select lets the user pick and clear a provider", () =>
      verifyProviderFilterMultiSelect(page));
    await test.step("[Positive] Provider filter shows a '2 selected' count when two providers are chosen", () =>
      verifyProviderFilterTwoSelected(page));
    await test.step("[Positive] Provider filter lets the user add a custom provider name", () =>
      verifyProviderFilterCustomName(page));
    await test.step("[Positive] Status filter multi-select lets the user pick a status", () =>
      verifyStatusFilterMultiSelect(page));
    await test.step("[Positive] Currency single-select picks a currency and resets to placeholder", () =>
      verifyCurrencySingleSelect(page));
    await test.step("[Positive] Payment flow single-select picks a flow and clears", () =>
      verifyPaymentFlowSingleSelect(page));
    await test.step("[Positive] More filters expands and collapses the extra filter rows", () =>
      verifyMoreFiltersExpandCollapse(page));
    await test.step("[Positive] More filters section lets the user type amount range, dates and IDs", () =>
      verifyMoreFiltersLetsUserType(page));
    await test.step("[Negative] Amount range validation surfaces when min > max", () =>
      verifyAmountRangeValidation(page));
    await test.step("[Negative] Date range validation surfaces when from > to", () =>
      verifyDateRangeValidation(page));
    await test.step("[Positive] Reset filters clears all filters and the active filter count", () =>
      verifyResetFiltersClearsAll(page));
    await test.step("[Positive] Refresh button re-fetches the payments list", () =>
      verifyRefreshButtonRefetches(page));
    await test.step("[Positive] Organization filter picks an organization and clears", () =>
      verifyOrganizationFilter(page));
    await test.step("[Positive] Rows per page selector changes the page size and triggers a refetch", () =>
      verifyRowsPerPageSelectorChanges(page));
    await test.step("[Positive] No matching payments shows the filtered empty state with a Clear filters button", () =>
      verifyNoMatchingPaymentsShowsFilteredEmptyState(page));
    await test.step("[Positive] Last refreshed timestamp appears after the first fetch completes", () =>
      verifyLastRefreshedTimestamp(page));
    await test.step("[Positive] Payment table renders rows with provider, amount, date, status and actions", () =>
      verifyPaymentTableRendersRows(page, holder));
    await test.step("[Positive] Sort by column toggles direction on repeated clicks", () =>
      verifySortByColumnTogglesDirection(page));
    await test.step("[Positive] Pagination enables Next when hasNextPage is true", () =>
      verifyPaginationEnablesNext(page, holder));
    await test.step("[Negative] Error state surfaces 'Payments could not be loaded' with Try again", () =>
      verifyErrorStateSurfacesTryAgain(page, holder));
    await test.step("[Positive] Refund action on a CAPTURED row opens the refund dialog", () =>
      verifyRefundActionOpensDialog(page));
  });
});
