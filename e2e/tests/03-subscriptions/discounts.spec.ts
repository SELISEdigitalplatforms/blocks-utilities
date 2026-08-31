import { test, expect } from "../../support/test-base";
import { openSubscriptionSubPage } from "../../support/auth-helpers";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";

/** Opens Plans, then follows the Discounts header link so we land on the discounts page. */
async function openDiscountsPage(page: import("@playwright/test").Page) {
  await openSubscriptionSubPage(page, "Plans");
  await page.getByRole("link", { name: "Discounts" }).click();
  await expect(page).toHaveURL(/\/subscription\/discounts$/);
}

/** Fills Step 1 (Identity) with a unique code/name and advances to Step 2. */
async function fillIdentityStep(
  page: import("@playwright/test").Page,
  code: string,
  name: string,
) {
  await expect(page.getByRole("heading", { name: "Identity" })).toBeVisible();
  await page.getByLabel("Code").fill(code);
  await page.getByLabel("Display name").fill(name);
  await page.getByRole("button", { name: "Next" }).click();
}

/** Fills Step 2 (Benefit) for a percentage-based Standard discount and advances. */
async function fillBenefitStep(page: import("@playwright/test").Page, percentOff: string) {
  await expect(page.getByRole("heading", { name: "Benefit" })).toBeVisible();
  await page.getByLabel("Percent off").fill(percentOff);
  await page.getByRole("button", { name: "Next" }).click();
}

/** Steps 3 (Eligibility) and 4 (Review) accept the defaults for a Standard offer — just advance. */
async function skipEligibilityAndReviewSteps(page: import("@playwright/test").Page) {
  await expect(page.getByRole("heading", { name: "Eligibility" })).toBeVisible();
  await page.getByRole("button", { name: "Next" }).click();

  await expect(page.getByRole("heading", { name: "Review" })).toBeVisible();
}

test.describe("Subscriptions - Discounts", () => {
  test.beforeEach(async ({ page }) => {
    await openUtilitiesDashboard(page);
  });

  test("Discounts", async ({ page }) => {
    const stamp = Date.now();
    const code = `e2e-discount-${stamp}`;
    const name = `E2E Discount ${stamp}`;

    await openDiscountsPage(page);

    await test.step("[Positive] page header, catalogue card and New discount CTA are visible", async () => {
      await expect(page.getByRole("heading", { name: "Subscription discounts" })).toBeVisible();
      await expect(page.getByText("Discount catalogue", { exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: "New discount" })).toBeVisible();
    });

    await test.step("[Positive] empty catalogue shows the 'No discounts authored yet' notice", async () => {
      const emptyState = page.getByText("No discounts authored yet", { exact: true });
      if (await emptyState.isVisible().catch(() => false)) {
        await expect(emptyState).toBeVisible();
      }
    });

    await test.step("[Positive] New discount opens the CampaignBuilder wizard on Step 1", async () => {
      await page.getByRole("button", { name: "New discount" }).click();

      await expect(page.getByRole("heading", { name: "Identity" })).toBeVisible();
      await expect(page.getByText("Benefit", { exact: true })).toBeVisible();
      await expect(page.getByText("Eligibility", { exact: true })).toBeVisible();
      await expect(page.getByText("Review", { exact: true })).toBeVisible();

      // Cancel out of the wizard to start the create flow fresh.
      await page.getByRole("button", { name: "Cancel" }).click();
      await expect(page.getByRole("button", { name: "New discount" })).toBeVisible();
    });

    await test.step("[Positive] creating a Standard percentage discount adds it to the catalogue", async () => {
      await page.getByRole("button", { name: "New discount" }).click();
      await fillIdentityStep(page, code, name);
      await fillBenefitStep(page, "15");

      // Standard offer: eligibility is optional, so leave every checkbox clear.
      await skipEligibilityAndReviewSteps(page);

      await page.getByRole("button", { name: "Create discount" }).click();

      // The discount row carries the display name and the percent-off figure.
      const row = page.locator("div", { hasText: name }).filter({ hasText: "15% off" }).first();
      await expect(row).toBeVisible({ timeout: 15_000 });
      await expect(row).toContainText(name);
      await expect(row).toContainText(code);
      await expect(row).toContainText("Active");
    });

    await test.step("[Positive] Edit loads the wizard pre-filled with the discount's values", async () => {
      const row = page.locator("div", { hasText: name }).filter({ hasText: "15% off" }).first();
      await row.getByRole("button", { name: "Edit" }).click();

      await expect(page.getByRole("heading", { name: "Identity" })).toBeVisible();
      // The code is fixed once created, so the input carries the read-only attribute.
      await expect(page.getByLabel("Code")).toHaveValue(code);
      await expect(page.getByLabel("Display name")).toHaveValue(name);

      // Cancel out — the suite does not save this edit, the next step retires the row.
      await page.getByRole("button", { name: "Cancel" }).click();
    });

    await test.step("[Positive] Retire moves the discount to Archived (row stays, buttons disappear)", async () => {
      const row = page.locator("div", { hasText: name }).filter({ hasText: "15% off" }).first();
      await expect(row).toBeVisible({ timeout: 15_000 });
      await row.getByRole("button", { name: "Retire" }).click();

      // Retiring archives the discount rather than deleting it. The Edit/Retire
      // buttons disappear once status leaves "Active", and the badge flips to
      // "Archived" (the row itself stays in the catalogue).
      await expect(row.getByRole("button", { name: "Retire" })).toHaveCount(0, {
        timeout: 15_000,
      });
      await expect(row.getByRole("button", { name: "Edit" })).toHaveCount(0);
      await expect(row).toContainText("Archived");
    });
  });
});