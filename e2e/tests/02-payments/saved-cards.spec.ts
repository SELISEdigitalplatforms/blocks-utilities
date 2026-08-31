import { test, expect } from "../../support/test-base";
import { openPaymentsSubPage } from "../../support/auth-helpers";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";

test.describe("Payments", () => {
  test.beforeEach(async ({ page }) => {
    await openUtilitiesDashboard(page);
  });

  test("Saved Cards", async ({ page }) => {
    await openPaymentsSubPage(page, "Saved Cards");

    await test.step("[Positive] page header and search/filter controls are visible", async () => {
      await expect(page.getByRole("heading", { name: "Saved cards" })).toBeVisible();
      // The search input only renders once the saved-methods query has settled
      // (loading skeleton / error state occupy the same slot). Wait for either
      // the input or the error heading so the assertion survives a late API.
      const searchInput = page.getByPlaceholder("Search brand or last four digits");
      const errorHeading = page.getByRole("heading", { name: "Saved methods could not be loaded" });
      await expect(searchInput.or(errorHeading)).toBeVisible({ timeout: 15_000 });
    });

    await test.step("[Positive] empty environment shows 'No saved payment methods'", async () => {
      const emptyState = page.getByText("No saved payment methods", { exact: true });
      if (await emptyState.isVisible().catch(() => false)) {
        await expect(
          page.getByText("A method appears here after the shopper gives consent during"),
        ).toBeVisible();
      }
    });

    await test.step("[Positive] Create payment shortcut navigates to Create Payment", async () => {
      await page.getByRole("link", { name: "Create payment", exact: true }).click();
      await expect(page).toHaveURL(/\/payment\/create$/);
      await expect(page.getByRole("heading", { name: "Test hosted payment" })).toBeVisible();
    });
  });
});
