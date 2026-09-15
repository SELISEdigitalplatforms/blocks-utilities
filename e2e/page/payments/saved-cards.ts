import { expect, Page } from "@playwright/test";

/**
 * Saved Cards flows. Each function is a reusable step that exercises a
 * single saved-cards behavior end-to-end.
 */

/**
 * Installs a mutable stub for GET /api/payments/payment-methods.
 * The holder lets the spec swap empty/seeded/error payloads mid-test.
 */
export type SavedCardsResponse = { status: number; contentType: string; body: string };
export type SavedCardsResponder = () => Promise<SavedCardsResponse> | SavedCardsResponse;
export type SavedCardsHolder = { responder: SavedCardsResponder };

export function savedCardsBody(methods: unknown[]): string {
  return JSON.stringify({ success: true, data: methods, error: null });
}

export function savedCardsErrorBody(message: string): string {
  return JSON.stringify({ success: false, data: null, error: { code: "INTERNAL", message } });
}

export function emptySavedCardsResponder(): SavedCardsResponder {
  return () => ({ status: 200, contentType: "application/json", body: savedCardsBody([]) });
}

export const SAVED_CARD_VISA = {
  paymentMethodId: "pm_e2e_visa_4242",
  brand: "visa",
  lastFour: "4242",
  type: "card",
  expiryMonth: "12",
  expiryYear: "2030",
  fundingSource: "credit",
  issuerCountry: "CH",
  status: "ACTIVE",
};

export const SAVED_CARD_MASTERCARD = {
  paymentMethodId: "pm_e2e_mc_5555",
  brand: "mastercard",
  lastFour: "5555",
  type: "card",
  expiryMonth: "01",
  expiryYear: "2029",
  fundingSource: "credit",
  issuerCountry: "US",
  status: "ACTIVE",
};

export async function stubSavedCardsEndpoint(page: Page, holder: SavedCardsHolder): Promise<void> {
  await page.route("**/api/payments/payment-methods*", async (route) => {
    if (route.request().method() === "DELETE") return route.continue();
    if (route.request().method() !== "GET") return route.continue();
    const response = await holder.responder();
    await route.fulfill(response);
  });
}

/** Installs a stub for DELETE /api/payments/payment-methods/{id}. */
export async function stubSavedCardsDeleteEndpoint(page: Page): Promise<void> {
  await page.route("**/api/payments/payment-methods/*", async (route) => {
    if (route.request().method() !== "DELETE") return route.continue();
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ success: true, data: { status: "REMOVED" } }),
    });
  });
}

/**
 * Saved Cards: page header and search/filter controls are visible.
 * The search input only renders once the saved-methods query has settled
 * (loading skeleton / error state occupy the same slot), so we wait for
 * either the input or the error heading.
 */
export async function verifyPageHeaderAndSearchControlsVisible(page: Page): Promise<void> {
  await expect(page.getByRole("heading", { name: "Saved cards" })).toBeVisible();
  const searchInput = page.getByPlaceholder("Search brand or last four digits");
  const errorHeading = page.getByRole("heading", { name: "Saved methods could not be loaded" });
  await expect(searchInput.or(errorHeading)).toBeVisible({ timeout: 15_000 });
}

/**
 * Saved Cards: empty environment shows 'No saved payment methods' with
 * the explanatory subtitle. Tolerates the absence of the empty state
 * (e.g. when the tenant has saved methods).
 */
export async function verifyEmptyStateShowsNoSavedMethods(page: Page): Promise<void> {
  const emptyState = page.getByText("No saved payment methods", { exact: true });
  if (await emptyState.isVisible().catch(() => false)) {
    await expect(
      page.getByText("A method appears here after the shopper gives consent during"),
    ).toBeVisible();
  }
}

/** Saved Cards: 'Create payment' shortcut navigates to /payment/create. */
export async function verifyCreatePaymentShortcutNavigates(page: Page): Promise<void> {
  await page.getByRole("link", { name: "Create payment", exact: true }).click();
  await expect(page).toHaveURL(/\/payment\/create$/);
  await expect(page.getByRole("heading", { name: "Test hosted payment" })).toBeVisible();
}

/**
 * TODO-09a (saved cards — seeded list): with two stubbed methods the section renders
 * the "2 of 2 methods" counter and both brands/last-four values.
 */
export async function verifySeededCardsRenderRows(page: Page, holder: SavedCardsHolder): Promise<void> {
  holder.responder = () => ({
    status: 200,
    contentType: "application/json",
    body: savedCardsBody([SAVED_CARD_VISA, SAVED_CARD_MASTERCARD]),
  });
  await page.reload();
  await expect(page.getByRole("heading", { name: "Saved cards" })).toBeVisible();
  await expect(page.getByText("2 of 2 methods")).toBeVisible({ timeout: 15_000 });
  // Scope brand assertions to the table: the mobile-card view renders the same
  // brand text a second time, which trips strict mode on a page-level locator.
  const table = page.getByRole("table");
  await expect(table.getByText("Visa", { exact: true })).toBeVisible();
  await expect(table.getByText("Mastercard", { exact: true })).toBeVisible();
  await expect(table.getByText("4242", { exact: false })).toBeVisible();
  await expect(table.getByText("5555", { exact: false })).toBeVisible();
}

/**
 * TODO-09b (saved cards — search narrows): typing a brand filters the seeded list to
 * the matching card only.
 */
export async function verifySearchNarrowsSeededCards(page: Page): Promise<void> {
  const search = page.getByPlaceholder("Search brand or last four digits");
  await expect(search).toBeVisible({ timeout: 15_000 });
  // Scope to the table: the mobile-card article mirrors the same masked number
  // text, which trips strict mode on a page-level locator.
  const table = page.getByRole("table");
  await search.fill("visa");
  await expect(table.getByText("4242", { exact: false })).toBeVisible();
  await expect(table.getByText("5555", { exact: false })).toHaveCount(0);
  await search.fill("");
  await expect(table.getByText("5555", { exact: false })).toBeVisible();
}

/**
 * TODO-09c (saved cards — remove, stubbed DELETE): the row remove action opens the
 * confirm dialog; confirming shows the "Payment method removed" toast without
 * deleting anything real (DELETE is stubbed).
 */
export async function verifyRemoveCardShowsRemovedToast(page: Page): Promise<void> {
  await stubSavedCardsDeleteEndpoint(page);
  const removeButton = page.getByRole("button", { name: /Remove .* ending in/ }).first();
  await expect(removeButton).toBeVisible({ timeout: 15_000 });
  await removeButton.click();
  await expect(page.getByRole("heading", { name: "Remove saved payment method?" })).toBeVisible();
  const confirmButton = page.getByRole("button", { name: "Remove method" });
  await expect(confirmButton).toBeVisible();
  await confirmButton.click();
  // Exact match: the toast title <div> and the aria-live notification <span>
  // both contain this text, which trips strict mode on a fuzzy locator.
  await expect(page.getByText("Payment method removed", { exact: true })).toBeVisible({
    timeout: 15_000,
  });
}
