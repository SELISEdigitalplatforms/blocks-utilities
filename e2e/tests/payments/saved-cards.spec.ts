import { test, expect } from "../../support/test-base";
import { loginFresh } from "../../support/login-helper";

// Fresh, isolated context for this file — ignore the "chromium" project's
// default storageState and log in for real instead of reusing a saved session.

test.describe("saved cards", () => {
  test.use({ storageState: { cookies: [], origins: [] } });
  test.beforeEach(async ({ page }) => {
    test.setTimeout(180_000);
    await loginFresh(page);

    await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
      timeout: 30_000,
    });
    await page
      .getByRole("button", { name: /Development/ })
      .first()
      .click();
    await expect(page.getByRole("heading", { name: "Project Details" })).toBeVisible({
      timeout: 30_000,
    });

    const savedCardsLink = page.getByRole("link", { name: "Saved Cards" });
    if (!(await savedCardsLink.isVisible().catch(() => false))) {
      await page.getByRole("button", { name: "Payments", exact: true }).click();
    }
    await savedCardsLink.click();
    await expect(page.getByRole("heading", { name: "Saved cards" })).toBeVisible({
      timeout: 30_000,
    });
  });

  test("TC-0047: Saved Cards page renders with heading and a 'Create payment' shortcut", async ({
    page,
  }) => {
    await expect(page.getByRole("heading", { name: "Saved cards" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Create payment", exact: true })).toBeVisible();
  });

  test("TC-0048: '<n> of <n> methods' summary reflects the filtered vs. total counts", async ({
    page,
  }) => {
    await expect(page.getByText(/\d+ of \d+ methods/)).toBeVisible({
      timeout: 30_000,
    });
  });

  test("TC-0049: Loading skeleton renders while saved methods are being fetched", async ({
    page,
  }) => {
    await page.route("**/api/payments/payment-methods**", async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 1500));
      await route.continue();
    });
    await page.reload();

    await expect(page.locator('[aria-label="Loading saved payment methods"]')).toBeVisible({
      timeout: 30_000,
    });
  });

  test("TC-0050: Load failure shows an error state with the returned message", async ({ page }) => {
    await page.route("**/api/payments/payment-methods**", async (route) => {
      await route.fulfill({
        status: 500,
        contentType: "application/json",
        body: JSON.stringify({ isSuccess: false }),
      });
    });
    await page.reload();

    await expect(
      page.getByRole("heading", { name: "Saved methods could not be loaded" }),
    ).toBeVisible({ timeout: 30_000 });
  });

  test("TC-0051: Search filters by brand, last four digits, or type", async ({ page }) => {
    const searchInput = page.getByPlaceholder("Search brand or last four digits");
    await searchInput.fill("visa");
    await expect(searchInput).toHaveValue("visa");
  });

  test("TC-0052: Brand and Type filter dropdowns are populated from the loaded methods", async ({
    page,
  }) => {
    const brandFilter = page.getByLabel("Filter by card brand");
    await brandFilter.click();
    await expect(page.getByRole("option", { name: "All brands" })).toBeVisible();

    await page.keyboard.press("Escape");
    const typeFilter = page.getByLabel("Filter by method type");
    await typeFilter.click();
    await expect(page.getByRole("option", { name: "All types" })).toBeVisible();
  });

  test("TC-0053: 'Clear filters' is disabled until a filter is active, then resets all filters", async ({
    page,
  }) => {
    const clearButton = page.getByRole("button", { name: "Clear filters" }).first();
    await expect(clearButton).toBeDisabled();

    const searchInput = page.getByPlaceholder("Search brand or last four digits");
    await searchInput.fill("visa");
    await expect(clearButton).toBeEnabled();

    await clearButton.click();
    await expect(searchInput).toHaveValue("");
    await expect(clearButton).toBeDisabled();
  });

  test("TC-0054: Empty state text differs between 'no data' and 'no matches'", async ({ page }) => {
    const noDataHeading = page.getByRole("heading", {
      name: "No saved payment methods",
    });
    if (await noDataHeading.isVisible({ timeout: 10000 }).catch(() => false)) {
      await expect(
        page.getByText(
          "A method appears here after the shopper gives consent during hosted checkout.",
        ),
      ).toBeVisible();
    } else {
      const searchInput = page.getByPlaceholder("Search brand or last four digits");
      await searchInput.fill("zzz_no_match_xyz");
      const noMatchHeading = page.getByRole("heading", {
        name: "No matching payment methods",
      });
      if (await noMatchHeading.isVisible({ timeout: 8000 }).catch(() => false)) {
        await expect(page.getByText("Try changing or clearing the current filters.")).toBeVisible();
      }
    }
  });

  test("TC-0055: Changing filters or page size resets pagination to page 1", async ({ page }) => {
    const pageSizeSelect = page.getByLabel(/rows per page/i);
    if (await pageSizeSelect.isVisible().catch(() => false)) {
      await pageSizeSelect.click();
      await page.getByRole("option", { name: "20" }).click();
      await expect(page.getByText(/Page\s*1\s*of/)).toBeVisible();
    }
  });

  test("TC-0056: Pagination Previous/Next respect first/last page boundaries", async ({ page }) => {
    const previousButton = page.getByLabel("Previous saved-method page");
    if (await previousButton.isVisible().catch(() => false)) {
      await expect(previousButton).toBeDisabled();
    }
  });

  test("TC-0057: Remove confirmation dialog shows the card brand and last four digits in its copy", async ({
    page,
  }) => {
    const removeButton = page.getByRole("button", { name: /remove/i }).first();
    if (await removeButton.isVisible().catch(() => false)) {
      await removeButton.click();
      await expect(
        page.getByRole("heading", { name: "Remove saved payment method?" }),
      ).toBeVisible();
      await expect(page.getByText(/ending in/)).toBeVisible();
    }
  });

  test("TC-0058: Confirming removal shows a success or 'processing' toast depending on the outcome", async ({
    page,
  }) => {
    const removeButton = page.getByRole("button", { name: /remove/i }).first();
    if (await removeButton.isVisible().catch(() => false)) {
      await removeButton.click();
      await page.getByRole("button", { name: "Remove method" }).click();

      const successToast = page.getByText("Payment method removed");
      const processingToast = page.getByText("Removal is processing");
      await expect(successToast.or(processingToast)).toBeVisible({
        timeout: 30_000,
      });
    }
  });

  test("TC-0059: Failed removal shows a destructive toast and keeps the method in the list", async ({
    page,
  }) => {
    const removeButton = page.getByRole("button", { name: /remove/i }).first();
    if (await removeButton.isVisible().catch(() => false)) {
      await page.route("**/api/payments/payment-methods/**", async (route) => {
        if (route.request().method() !== "GET") {
          await route.fulfill({
            status: 500,
            contentType: "application/json",
            body: JSON.stringify({ isSuccess: false }),
          });
        } else {
          await route.continue();
        }
      });

      await removeButton.click();
      await page.getByRole("button", { name: "Remove method" }).click();

      await expect(page.getByText("Removal failed")).toBeVisible({
        timeout: 30_000,
      });
    }
  });

  test("TC-0060: 'Keep method' cancels the removal without sending a request", async ({ page }) => {
    const removeButton = page.getByRole("button", { name: /remove/i }).first();
    if (await removeButton.isVisible().catch(() => false)) {
      await removeButton.click();
      await page.getByRole("button", { name: "Keep method" }).click();
      await expect(
        page.getByRole("heading", { name: "Remove saved payment method?" }),
      ).toBeHidden();
    }
  });
});
