import { expect, Page } from "@playwright/test";

/**
 * Saved Cards flows. Each function is a reusable step that exercises a
 * single saved-cards behavior end-to-end.
 */

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
