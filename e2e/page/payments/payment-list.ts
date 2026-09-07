import { expect, type Page } from "@playwright/test";
import { openPaymentsSubPage } from "../../support/auth-helpers";

/**
 * Payment List flows. Each function is a reusable step that exercises a
 * single payment-list behavior end-to-end (the click, the fill, the assertion).
 *
 * The /api/payments endpoint is stubbed via a mutable responder holder so
 * flows can swap response bodies (empty / seeded rows / pagination / errors)
 * without re-installing the route. The spec creates the holder and passes
 * it to flows that need to mutate the responder.
 *
 * Assumes the caller has already opened the project dashboard via
 * openUtilitiesDashboard(page) so the sidebar is visible.
 */

export type PaymentsListResponse = {
  status: number;
  contentType: string;
  body: string;
};

/** A responder may be sync or async. */
export type PaymentsListResponder = () =>
  | Promise<PaymentsListResponse>
  | PaymentsListResponse;

export type PaymentsListHolder = {
  responder: PaymentsListResponder;
};

/** Wrap a payments-list payload in the standard success envelope. */
export function paymentsListBody(items: unknown[], pageInfo: unknown): string {
  return JSON.stringify({
    success: true,
    data: {
      items,
      pageInfo,
    },
  });
}

/** Wrap an error in the standard failure envelope. */
export function paymentsListErrorBody(message: string): string {
  return JSON.stringify({
    success: false,
    data: null,
    error: { code: "INTERNAL", message },
  });
}

/**
 * Install a mutable responder for /api/payments (GET only). Non-GET
 * requests pass through.
 */
export async function stubPaymentsListEndpoint(
  page: Page,
  holder: PaymentsListHolder,
): Promise<void> {
  await page.route("**/api/payments**", async (route) => {
    if (route.request().method() !== "GET") {
      return route.continue();
    }
    const response = await holder.responder();
    await route.fulfill(response);
  });
}

/** Install a stub for the organizations endpoint used by the Organization filter. */
export async function stubPaymentsListOrganizationsEndpoint(page: Page): Promise<void> {
  await page.route("**/organizations**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        organizations: [
          { itemId: "org-acme", name: "Acme Corp" },
          { itemId: "org-globex", name: "Globex Inc" },
        ],
        totalCount: 2,
      }),
    });
  });
}

/** Default empty responder (no payments yet). */
export function emptyPaymentsListResponder(): PaymentsListResponder {
  return () => ({
    status: 200,
    contentType: "application/json",
    body: paymentsListBody([], {
      hasNextPage: false,
      hasPreviousPage: false,
      startCursor: null,
      endCursor: null,
    }),
  });
}

/** Navigate to the Payment List sub-page. */
export async function openPaymentList(page: Page): Promise<void> {
  await openPaymentsSubPage(page, "Payment List");
}

/** List header shows the live status badge and filters. */
export async function verifyListHeaderAndFilters(page: Page): Promise<void> {
  await expect(page.getByRole("heading", { name: "Payment list" })).toBeVisible();
  await expect(page.getByText("Live", { exact: true })).toBeVisible();
  await expect(page.getByText("Filter payments", { exact: true })).toBeVisible();
}

/** Filter fields cover provider/status/currency/flow/organization. */
export async function verifyFilterFieldsPresent(page: Page): Promise<void> {
  await expect(page.getByText("All providers", { exact: true })).toBeVisible();
  await expect(page.getByText("All statuses", { exact: true })).toBeVisible();
  await expect(
    page.getByRole("combobox").filter({ hasText: "All currencies" }),
  ).toBeVisible();
  await expect(page.getByRole("combobox").filter({ hasText: "All flows" })).toBeVisible();
  await expect(
    page.getByRole("combobox").filter({ hasText: "All organizations" }),
  ).toBeVisible();
}

/** Apply filters can be triggered without error. */
export async function verifyApplyFiltersCanBeTriggered(page: Page): Promise<void> {
  await page.getByRole("button", { name: "Apply filters" }).click();
  await expect(page.getByRole("heading", { name: "Payment list" })).toBeVisible();
}

/** Empty environment shows 'No payments yet' with the explanatory subtitle. */
export async function verifyEmptyEnvironmentShowsNoPaymentsYet(page: Page): Promise<void> {
  await expect(page.getByText("No payments yet", { exact: true })).toBeVisible();
  await expect(
    page.getByText("Payments will appear here as soon as they are created.", {
      exact: true,
    }),
  ).toBeVisible();
}

/**
 * Provider filter multi-select lets the user pick and clear a provider.
 * Anchors on the Providers label and walks up to its enclosing wrapper
 * so scoped locators don't bleed into sibling filter groups.
 */
export async function verifyProviderFilterMultiSelect(page: Page): Promise<void> {
  const providersGroup = page.getByText("Providers", { exact: true }).locator("xpath=..");

  const providersTrigger = providersGroup.getByRole("button").first();
  await expect(providersTrigger).toHaveText("All providers");

  await providersTrigger.click();
  const adyenCheckbox = page.getByRole("checkbox", { name: "Select ADYEN-ONLINE" });
  await expect(adyenCheckbox).toBeVisible();

  // Pick ADYEN-ONLINE — the trigger label should switch from
  // "All providers" to the selected value (single selection shows value).
  await adyenCheckbox.click();
  await expect(adyenCheckbox).toBeChecked();
  await expect(
    providersGroup.getByRole("button", { name: "ADYEN-ONLINE" }),
  ).toBeVisible();

  // Close the popover so subsequent steps can locate their own controls.
  await page.keyboard.press("Escape");

  // Clear the filter by reopening and unchecking.
  await providersGroup.getByRole("button", { name: "ADYEN-ONLINE" }).click();
  await adyenCheckbox.click();
  await expect(adyenCheckbox).not.toBeChecked();

  // Trigger should be back to its empty-state label.
  await expect(providersGroup.getByRole("button").first()).toHaveText("All providers");
  await page.keyboard.press("Escape");
}

/**
 * Provider filter shows a '2 selected' count when two providers are
 * chosen.
 */
export async function verifyProviderFilterTwoSelected(page: Page): Promise<void> {
  const providersGroup = page.getByText("Providers", { exact: true }).locator("xpath=..");
  const providersTrigger = providersGroup.getByRole("button").first();
  await providersTrigger.click();

  // Pick the only suggested provider first.
  // Radix checkboxes are <button role="checkbox"> elements; use click()
  // for both check and uncheck since .check()/.uncheck() can hang on
  // Radix-managed state.
  const adyenCheckbox = page.getByRole("checkbox", { name: "Select ADYEN-ONLINE" });
  await adyenCheckbox.click();
  await expect(adyenCheckbox).toBeChecked();

  // Add a custom provider via the input — the popover surfaces a new
  // checkbox once the value is added (component dedupes via Set).
  // Use pressSequentially instead of fill because Playwright's fill()
  // sets the value atomically and React 18 sometimes batches the
  // onChange update so the component closure sees an empty customValue
  // when Enter is dispatched.
  const customInput = page.getByPlaceholder("Add provider name");
  await expect(customInput).toBeVisible();
  await customInput.click();
  await customInput.pressSequentially("stripe", { delay: 25 });
  await expect(customInput).toHaveValue("stripe");
  // Use the explicit "Add provider" button — its onClick handler reads
  // the current customValue via the component closure rather than the
  // keydown handler, sidestepping any React batching between fill and
  // keydown event firing.
  await page.getByRole("button", { name: "Add provider" }).click();

  const stripeCheckbox = page.getByRole("checkbox", { name: "Select STRIPE" });
  await expect(stripeCheckbox).toBeChecked();

  // Two selections → trigger label switches to "<count> selected".
  await expect(
    providersGroup.getByRole("button", { name: "2 selected" }),
  ).toBeVisible();

  // Uncheck one → falls back to the single-value label.
  await stripeCheckbox.click();
  await expect(
    providersGroup.getByRole("button", { name: "ADYEN-ONLINE" }),
  ).toBeVisible();

  // Reset to empty before closing.
  await adyenCheckbox.click();
  await expect(adyenCheckbox).not.toBeChecked();
  await expect(providersGroup.getByRole("button").first()).toHaveText("All providers");
  await page.keyboard.press("Escape");
}

/**
 * Provider filter lets the user add a custom provider name. Mixed-case
 * input is upper-cased before being added; duplicates are silently
 * ignored.
 */
export async function verifyProviderFilterCustomName(page: Page): Promise<void> {
  const providersGroup = page.getByText("Providers", { exact: true }).locator("xpath=..");
  const providersTrigger = providersGroup.getByRole("button").first();
  await providersTrigger.click();

  const customInput = page.getByPlaceholder("Add provider name");
  await expect(customInput).toBeVisible();
  await customInput.click();

  await customInput.pressSequentially("worldpay", { delay: 25 });
  await expect(customInput).toHaveValue("worldpay");
  await page.getByRole("button", { name: "Add provider" }).click();

  // Trigger label and the new checkbox reflect the normalized value.
  await expect(
    providersGroup.getByRole("button", { name: "WORLDPAY" }),
  ).toBeVisible();
  await expect(page.getByRole("checkbox", { name: "Select WORLDPAY" })).toBeChecked();

  // Enter key in the input also adds the value.
  await customInput.pressSequentially("adyen-test", { delay: 25 });
  await expect(customInput).toHaveValue("adyen-test");
  await customInput.press("Enter");
  await expect(
    providersGroup.getByRole("button", { name: "2 selected" }),
  ).toBeVisible();

  // Duplicate submission is silently ignored — still 2 selected, not 3.
  await customInput.pressSequentially("WORLDPAY", { delay: 25 });
  await expect(customInput).toHaveValue("WORLDPAY");
  await customInput.press("Enter");
  await expect(
    providersGroup.getByRole("button", { name: "2 selected" }),
  ).toBeVisible();

  // Clear all custom + standard selections to leave the filter clean.
  await page.getByRole("checkbox", { name: "Select WORLDPAY" }).click();
  await page.getByRole("checkbox", { name: "Select ADYEN-TEST" }).click();
  await expect(providersGroup.getByRole("button").first()).toHaveText("All providers");
  await page.keyboard.press("Escape");
}

/**
 * Status filter multi-select lets the user pick one or more statuses.
 * Trigger label switches to single-value on one pick and "<count> selected"
 * on two.
 */
export async function verifyStatusFilterMultiSelect(page: Page): Promise<void> {
  const statusesGroup = page.getByText("Statuses", { exact: true }).locator("xpath=..");
  const statusesTrigger = statusesGroup.getByRole("button").first();
  await expect(statusesTrigger).toHaveText("All statuses");

  await statusesTrigger.click();
  const authorizedCheckbox = page.getByRole("checkbox", { name: "Select AUTHORIZED" });
  await expect(authorizedCheckbox).toBeVisible();

  // Pick AUTHORIZED — the trigger label should switch to the selected value.
  await authorizedCheckbox.click();
  await expect(authorizedCheckbox).toBeChecked();
  await expect(statusesGroup.getByRole("button", { name: "AUTHORIZED" })).toBeVisible();

  // Pick a second status to verify the count label kicks in.
  const capturedCheckbox = page.getByRole("checkbox", { name: "Select CAPTURED" });
  await capturedCheckbox.click();
  await expect(capturedCheckbox).toBeChecked();
  await expect(statusesGroup.getByRole("button", { name: "2 selected" })).toBeVisible();

  // Clear back to empty so subsequent steps find their own controls.
  await capturedCheckbox.click();
  await expect(statusesGroup.getByRole("button", { name: "AUTHORIZED" })).toBeVisible();
  await authorizedCheckbox.click();
  await expect(statusesGroup.getByRole("button").first()).toHaveText("All statuses");
  await page.keyboard.press("Escape");
}

/** Currency single-select picks a currency and resets to placeholder. */
export async function verifyCurrencySingleSelect(page: Page): Promise<void> {
  const currencyGroup = page.getByText("Currency", { exact: true }).locator("xpath=..");

  // Radix Select renders a combobox — placeholder is shown when empty.
  const currencyTrigger = currencyGroup.getByRole("combobox");
  await expect(currencyTrigger).toHaveText("All currencies");

  await currencyTrigger.click();
  // USD option is exposed in the listbox with code — name label.
  await page.getByRole("option", { name: "USD — US Dollar" }).click();

  await expect(currencyTrigger).toHaveText("USD — US Dollar");

  // Reopen and pick the "All currencies" sentinel to reset.
  await currencyTrigger.click();
  await page.getByRole("option", { name: "All currencies" }).click();
  await expect(currencyTrigger).toHaveText("All currencies");
}

/** Payment flow single-select picks a flow and clears. */
export async function verifyPaymentFlowSingleSelect(page: Page): Promise<void> {
  const flowGroup = page.getByText("Payment flow", { exact: true }).locator("xpath=..");

  const flowTrigger = flowGroup.getByRole("combobox");
  await expect(flowTrigger).toHaveText("All flows");

  await flowTrigger.click();
  await page.getByRole("option", { name: "Hosted checkout" }).click();
  await expect(flowTrigger).toHaveText("Hosted checkout");

  // Clear via the "All flows" sentinel option.
  await flowTrigger.click();
  await page.getByRole("option", { name: "All flows" }).click();
  await expect(flowTrigger).toHaveText("All flows");
}

/** More filters expands and collapses the extra filter rows. */
export async function verifyMoreFiltersExpandCollapse(page: Page): Promise<void> {
  const moreFiltersButton = page.getByRole("button", { name: "More filters" });
  await expect(moreFiltersButton).toBeVisible();

  // Collapsed by default — the date/payment-id inputs must be hidden.
  await expect(page.locator('input[id="payment-date-from"]')).toHaveCount(0);
  await expect(page.locator('input[id="payment-order-id"]')).toHaveCount(0);

  await moreFiltersButton.click();
  await expect(page.getByRole("button", { name: "Fewer filters" })).toBeVisible();
  // Inputs are now mounted in the DOM.
  await expect(page.locator('input[id="payment-min-amount"]')).toBeVisible();
  await expect(page.locator('input[id="payment-max-amount"]')).toBeVisible();
  await expect(page.locator('input[id="payment-date-from"]')).toBeVisible();
  await expect(page.locator('input[id="payment-date-to"]')).toBeVisible();
  await expect(page.locator('input[id="payment-order-id"]')).toBeVisible();
  await expect(page.locator('input[id="payment-detail-id"]')).toBeVisible();

  await page.getByRole("button", { name: "Fewer filters" }).click();
  await expect(page.getByRole("button", { name: "More filters" })).toBeVisible();
  await expect(page.locator('input[id="payment-order-id"]')).toHaveCount(0);
}

/**
 * More filters section lets the user type amount range, dates and IDs.
 * Verifies that the active-filter count chip appears with the right value.
 */
export async function verifyMoreFiltersLetsUserType(page: Page): Promise<void> {
  await page.getByRole("button", { name: "More filters" }).click();

  await page.locator('input[id="payment-min-amount"]').fill("10");
  await page.locator('input[id="payment-max-amount"]').fill("500");
  await page.locator('input[id="payment-date-from"]').fill("2025-01-01");
  await page.locator('input[id="payment-date-to"]').fill("2025-12-31");
  await page.locator('input[id="payment-order-id"]').fill("ORD-1001");
  await page.locator('input[id="payment-detail-id"]').fill("pd_abc123");

  await expect(page.locator('input[id="payment-min-amount"]')).toHaveValue("10");
  await expect(page.locator('input[id="payment-max-amount"]')).toHaveValue("500");
  await expect(page.locator('input[id="payment-date-from"]')).toHaveValue("2025-01-01");
  await expect(page.locator('input[id="payment-date-to"]')).toHaveValue("2025-12-31");
  await expect(page.locator('input[id="payment-order-id"]')).toHaveValue("ORD-1001");
  await expect(page.locator('input[id="payment-detail-id"]')).toHaveValue("pd_abc123");

  // Active filter chip near the heading should now reflect the count
  // of non-empty filter fields (6 of the more-filters inputs).
  await expect(
    page
      .locator("form span")
      .filter({ hasText: /^[0-9]+$/ })
      .first(),
  ).toBeVisible();

  // Collapse again to leave the page clean for subsequent steps.
  await page.getByRole("button", { name: "Fewer filters" }).click();
}

/** Amount range validation surfaces when min > max. */
export async function verifyAmountRangeValidation(page: Page): Promise<void> {
  // Open the more filters section so the amount inputs are mounted.
  await page.getByRole("button", { name: "More filters" }).click();

  await page.locator('input[id="payment-min-amount"]').fill("500");
  await page.locator('input[id="payment-max-amount"]').fill("100");

  // Submit triggers the client-side validator.
  await page.getByRole("button", { name: "Apply filters" }).click();

  await expect(
    page.getByRole("alert").filter({
      hasText: "Maximum amount must be greater than or equal to minimum amount.",
    }),
  ).toBeVisible();

  // Fix the range — submitting again clears the validation error.
  await page.locator('input[id="payment-max-amount"]').fill("1000");
  await page.getByRole("button", { name: "Apply filters" }).click();
  await expect(
    page.getByRole("alert").filter({
      hasText: "Maximum amount must be greater than or equal to minimum amount.",
    }),
  ).toHaveCount(0);

  // Reset state for the next step.
  await page.locator('input[id="payment-min-amount"]').fill("");
  await page.locator('input[id="payment-max-amount"]').fill("");
  await page.getByRole("button", { name: "Fewer filters" }).click();
}

/** Date range validation surfaces when from > to. */
export async function verifyDateRangeValidation(page: Page): Promise<void> {
  await page.getByRole("button", { name: "More filters" }).click();

  await page.locator('input[id="payment-date-from"]').fill("2025-12-31");
  await page.locator('input[id="payment-date-to"]').fill("2025-01-01");

  await page.getByRole("button", { name: "Apply filters" }).click();
  await expect(
    page.getByRole("alert").filter({
      hasText: "The end date must be the same as or later than the start date.",
    }),
  ).toBeVisible();

  // Fix it and resubmit.
  await page.locator('input[id="payment-date-to"]').fill("2026-12-31");
  await page.getByRole("button", { name: "Apply filters" }).click();
  await expect(
    page.getByRole("alert").filter({
      hasText: "The end date must be the same as or later than the start date.",
    }),
  ).toHaveCount(0);

  await page.locator('input[id="payment-date-from"]').fill("");
  await page.locator('input[id="payment-date-to"]').fill("");
  await page.getByRole("button", { name: "Fewer filters" }).click();
}

/** Reset filters clears all filters and the active filter count. */
export async function verifyResetFiltersClearsAll(page: Page): Promise<void> {
  // Set up a few filters first so we can verify reset wipes them out.
  const providersGroup = page.getByText("Providers", { exact: true }).locator("xpath=..");
  await providersGroup.getByRole("button").first().click();
  const adyenCheckbox = page.getByRole("checkbox", { name: "Select ADYEN-ONLINE" });
  await adyenCheckbox.click();
  await expect(adyenCheckbox).toBeChecked();
  await page.keyboard.press("Escape");

  // Currency selector picks USD.
  const currencyGroup = page.getByText("Currency", { exact: true }).locator("xpath=..");
  const currencyTrigger = currencyGroup.getByRole("combobox");
  await currencyTrigger.click();
  await page.getByRole("option", { name: "USD — US Dollar" }).click();
  await expect(currencyTrigger).toHaveText("USD — US Dollar");

  // Active filter chip is now showing a non-zero count.
  const filterForm = page.locator('form:has(:text("Filter payments"))');
  const countChip = filterForm
    .locator("span")
    .filter({ hasText: /^[0-9]+$/ })
    .first();
  await expect(countChip).toBeVisible();

  // Click Reset → chip disappears and trigger labels return to placeholder.
  await filterForm.getByRole("button", { name: "Reset" }).click();
  await expect(countChip).toHaveCount(0);
  await expect(providersGroup.getByRole("button").first()).toHaveText("All providers");
  await expect(currencyTrigger).toHaveText("All currencies");
}

/** Refresh button re-fetches the payments list. */
export async function verifyRefreshButtonRefetches(page: Page): Promise<void> {
  const refreshButton = page.getByRole("button", { name: "Refresh" });
  await expect(refreshButton).toBeVisible();

  // Clicking Refresh must produce a GET request to the payments endpoint.
  const responsePromise = page.waitForResponse(
    (resp) => resp.url().includes("/api/payments") && resp.request().method() === "GET",
  );
  await refreshButton.click();
  const response = await responsePromise;
  expect(response.status()).toBe(200);
}

/** Organization filter picks an organization and clears. */
export async function verifyOrganizationFilter(page: Page): Promise<void> {
  // Organizations endpoint was stubbed before navigation so the dropdown
  // has two seeded entries: Acme Corp and Globex Inc.
  const orgGroup = page.getByText("Organization", { exact: true }).locator("xpath=..");
  const orgTrigger = orgGroup.getByRole("combobox");
  await expect(orgTrigger).toHaveText("All organizations");

  await orgTrigger.click();
  await page.getByRole("option", { name: "Acme Corp" }).click();
  await expect(orgTrigger).toHaveText("Acme Corp");

  // Clear via the "All organizations" sentinel option.
  await orgTrigger.click();
  await page.getByRole("option", { name: "All organizations" }).click();
  await expect(orgTrigger).toHaveText("All organizations");
}

/**
 * Rows per page selector changes the page size and triggers a refetch.
 * Resets back to 25 to keep subsequent steps deterministic.
 */
export async function verifyRowsPerPageSelectorChanges(page: Page): Promise<void> {
  // Capture the request URL so we can verify the pageSize filter change.
  const pageSizeResponsePromise = page.waitForResponse(
    (resp) => resp.url().includes("/api/payments") && resp.request().method() === "GET",
  );

  const pageSizeSelect = page.locator("#payment-page-size");
  await pageSizeSelect.click();
  // 10 is one of PAYMENT_PAGE_SIZE_OPTIONS — exact match avoids colliding
  // with the "100" entry.
  await page.getByRole("option", { name: "10", exact: true }).click();

  await expect(pageSizeSelect).toHaveText("10");
  const response = await pageSizeResponsePromise;
  expect(response.status()).toBe(200);

  // Reset to 25 to keep subsequent steps deterministic.
  await pageSizeSelect.click();
  await page.getByRole("option", { name: "25", exact: true }).click();
  await expect(pageSizeSelect).toHaveText("25");
}

/**
 * No matching payments shows the filtered empty state with a Clear filters
 * button. Clicking the button returns the placeholder empty state.
 */
export async function verifyNoMatchingPaymentsShowsFilteredEmptyState(page: Page): Promise<void> {
  // With the empty stub still in place, applying any filter should
  // surface the filtered-empty state and offer a Clear filters button.
  await page
    .getByText("Statuses", { exact: true })
    .locator("xpath=..")
    .getByRole("button")
    .first()
    .click();
  await page.getByRole("checkbox", { name: "Select REFUNDED" }).click();
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: "Apply filters" }).click();

  await expect(page.getByText("No matching payments", { exact: true })).toBeVisible();
  await expect(
    page.getByText("Try adjusting or clearing the current filters.", { exact: true }),
  ).toBeVisible();

  // Clear filters button is offered in the filtered-empty state.
  const clearButton = page.getByRole("button", { name: "Clear filters" });
  await expect(clearButton).toBeVisible();

  await clearButton.click();
  // After clearing, the placeholder empty state returns.
  await expect(page.getByText("No payments yet", { exact: true })).toBeVisible();
}

/** Last refreshed timestamp appears after the first fetch completes. */
export async function verifyLastRefreshedTimestamp(page: Page): Promise<void> {
  // The route stub already triggered one fetch during navigation; the page
  // shows the timestamp next to the description. Format: "Last refreshed at HH:MM".
  await expect(
    page.getByText(/^Last refreshed at \d{1,2}:\d{2}/, { exact: false }),
  ).toBeVisible();
}

/**
 * Payment table renders rows with provider, amount, date, status and
 * actions. Swaps the responder to a 2-row seeded payload, then refetches.
 */
export async function verifyPaymentTableRendersRows(
  page: Page,
  holder: PaymentsListHolder,
): Promise<void> {
  const now = new Date().toISOString();
  holder.responder = () => ({
    status: 200,
    contentType: "application/json",
    body: paymentsListBody(
      [
        {
          paymentDetailId: "pd_aaaaaaaa1111bbbbbbbb",
          providerName: "ADYEN-ONLINE",
          amount: 250.5,
          currencyCode: "USD",
          paymentDateUtc: now,
          paymentStatus: "CAPTURED",
          hasPendingRefund: false,
        },
        {
          paymentDetailId: "pd_cccccccc2222dddddddd",
          providerName: "ADYEN-ONLINE",
          amount: 99.99,
          currencyCode: "EUR",
          paymentDateUtc: now,
          paymentStatus: "AUTHORIZED",
          hasPendingRefund: false,
        },
      ],
      {
        hasNextPage: false,
        hasPreviousPage: false,
        startCursor: null,
        endCursor: null,
      },
    ),
  });
  await page.getByRole("button", { name: "Refresh" }).click();

  // Header columns.
  await expect(page.getByRole("button", { name: /Provider/ }).first()).toBeVisible();
  await expect(page.getByRole("button", { name: /Amount/ }).first()).toBeVisible();
  await expect(page.getByRole("button", { name: /Payment date/ }).first()).toBeVisible();
  await expect(page.getByRole("button", { name: /Status/ }).first()).toBeVisible();

  // Row content — short ids, provider names, status badges.
  // shortenPaymentId truncates to "<first10>…<last6>".
  // Scope to the table so we don't collide with the mobile-card view.
  const table = page.getByRole("table");
  await expect(table.getByText("pd_aaaaaaa…bbbbbb", { exact: true })).toBeVisible();
  await expect(table.getByText("pd_ccccccc…dddddd", { exact: true })).toBeVisible();
  // Status badges render the human-friendly label, not the raw enum.
  await expect(table.getByText("Captured", { exact: true })).toBeVisible();
  await expect(table.getByText("Authorized", { exact: true })).toBeVisible();
  // Both seeded rows have refundable statuses.
  await expect(page.getByRole("button", { name: "Refund" }).first()).toBeVisible();

  // Counter line above the table reflects the row count.
  await expect(page.getByText("2 payments on this page", { exact: true })).toBeVisible();
}

/** Sort by column toggles direction on repeated clicks. */
export async function verifySortByColumnTogglesDirection(page: Page): Promise<void> {
  const providerHeader = page.getByRole("button", { name: /Provider/ }).first();
  // First click switches to asc because the default sort is paymentDate desc.
  await providerHeader.click();
  // After sort change, the table re-renders the sort indicator on the
  // active header. Active header uses `text-foreground` (vs `text-muted-foreground`).
  await expect(providerHeader).toHaveClass(/text-foreground/);

  // Second click toggles to desc.
  await providerHeader.click();
  await expect(providerHeader).toHaveClass(/text-foreground/);
}

/** Pagination enables Next when hasNextPage is true. */
export async function verifyPaginationEnablesNext(
  page: Page,
  holder: PaymentsListHolder,
): Promise<void> {
  holder.responder = () => ({
    status: 200,
    contentType: "application/json",
    body: paymentsListBody(
      [
        {
          paymentDetailId: "pd_page1row11111111111",
          providerName: "ADYEN-ONLINE",
          amount: 10,
          currencyCode: "USD",
          paymentDateUtc: new Date().toISOString(),
          paymentStatus: "CAPTURED",
          hasPendingRefund: false,
        },
      ],
      {
        hasNextPage: true,
        hasPreviousPage: false,
        startCursor: "cursor-start-1",
        endCursor: "cursor-end-1",
      },
    ),
  });
  await page.getByRole("button", { name: "Refresh" }).click();

  await expect(page.getByText("Page 1", { exact: true })).toBeVisible();
  // Next is enabled, Previous is disabled (no previous page yet).
  const previousButton = page.getByRole("button", { name: "Previous" });
  const nextButton = page.getByRole("button", { name: "Next" });
  await expect(previousButton).toBeDisabled();
  await expect(nextButton).toBeEnabled();

  await nextButton.click();
  await expect(page.getByText("Page 2", { exact: true })).toBeVisible();
}

/**
 * Error state surfaces 'Payments could not be loaded' with Try again.
 * Restores a happy-path responder and clicks Try again to recover.
 */
export async function verifyErrorStateSurfacesTryAgain(
  page: Page,
  holder: PaymentsListHolder,
): Promise<void> {
  // Apply a filter first so the Reset button becomes enabled — without
  // any active filter the Reset control is disabled.
  await page
    .getByText("Statuses", { exact: true })
    .locator("xpath=..")
    .getByRole("button")
    .first()
    .click();
  await page.getByRole("checkbox", { name: "Select REFUNDED" }).click();
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: "Apply filters" }).click();

  // Reset filters so the empty-state error renders instead of the
  // filtered-empty branch.
  const filterForm = page.locator('form:has(:text("Filter payments"))');
  await filterForm.getByRole("button", { name: "Reset" }).click();

  // Switch the responder to a failing one and reload the page so the
  // hook starts with no cached entry — the page's error UI only renders
  // when `isError && !data`, and TanStack keeps previous data on
  // refetch, so a soft Refresh wouldn't surface the error UI.
  holder.responder = () => ({
    status: 500,
    contentType: "application/json",
    body: paymentsListErrorBody("Boom"),
  });
  await page.reload();
  await page.getByRole("heading", { name: "Payment list" }).waitFor();

  await expect(
    page.getByText("Payments could not be loaded", { exact: true }),
  ).toBeVisible();
  const tryAgainButton = page.getByRole("button", { name: "Try again" });
  await expect(tryAgainButton).toBeVisible();

  // Restore the happy-path responder and click Try again — the table
  // should re-render with the seeded rows from the previous step.
  holder.responder = () => ({
    status: 200,
    contentType: "application/json",
    body: paymentsListBody(
      [
        {
          paymentDetailId: "pd_recover00000000000000",
          providerName: "ADYEN-ONLINE",
          amount: 50,
          currencyCode: "USD",
          paymentDateUtc: new Date().toISOString(),
          paymentStatus: "CAPTURED",
          hasPendingRefund: false,
        },
      ],
      {
        hasNextPage: false,
        hasPreviousPage: false,
        startCursor: null,
        endCursor: null,
      },
    ),
  });
  await tryAgainButton.click();
  // Scope to the table — desktop and mobile renderings both show the id.
  const table = page.getByRole("table");
  await expect(table.getByText("pd_recover…000000", { exact: true })).toBeVisible();
}

/** Refund action on a CAPTURED row opens the refund dialog. */
export async function verifyRefundActionOpensDialog(page: Page): Promise<void> {
  // The previous step left one seeded CAPTURED row in the table.
  const refundButton = page.getByRole("button", { name: "Refund" }).first();
  await expect(refundButton).toBeVisible();
  await refundButton.click();

  // Dialog renders the amount, reason field, and action buttons.
  await expect(page.getByRole("heading", { name: "Refund payment" })).toBeVisible();
  await expect(page.getByLabel("Refund amount")).toBeVisible();
  await expect(page.getByLabel(/^Reason/)).toBeVisible();
  await expect(page.getByRole("button", { name: "Cancel" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Confirm refund" })).toBeVisible();

  // Cancel closes the dialog without submitting.
  await page.getByRole("button", { name: "Cancel" }).click();
  await expect(page.getByRole("heading", { name: "Refund payment" })).toHaveCount(0);
}
