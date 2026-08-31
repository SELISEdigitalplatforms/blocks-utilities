import { test, expect } from "../../support/test-base";
import type { Page } from "@playwright/test";
import { openSubscriptionSubPage } from "../../support/auth-helpers";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";

/** Fills Step 1 (Identity) with a unique display name/code and advances to Step 2. */
async function fillIdentityStep(page: Page, displayName: string, code: string) {
  await expect(page.getByRole("heading", { name: "Identity" })).toBeVisible();
  await page.getByLabel("Display name").fill(displayName);
  await page.getByRole("textbox", { name: "Code", exact: true }).fill(code);
  await page.getByRole("button", { name: "Next" }).click();
}

/** Fills Step 2 (Pricing model) with just the required flat-fee amount and advances. */
async function fillPricingStep(page: Page, amount: string) {
  await expect(page.getByRole("heading", { name: "Pricing model" })).toBeVisible();
  await page.getByPlaceholder("89.00").first().fill(amount);
  await page.getByRole("button", { name: "Next" }).click();
}

/** Steps 3 (usage limits) and 4 (trial) are both fully optional — just advance through them. */
async function skipUsageLimitsAndTrialSteps(page: Page) {
  await expect(page.getByRole("heading", { name: "What the plan grants" })).toBeVisible();
  await page.getByRole("button", { name: "Next" }).click();

  await expect(page.getByRole("heading", { name: "Trial" })).toBeVisible();
  await page.getByRole("button", { name: "Next" }).click();
}

test.describe("Subscriptions - Plans", () => {
  test.beforeEach(async ({ page }) => {
    await openUtilitiesDashboard(page);
  });

  test("Plans", async ({ page }) => {
    const uniqueSuffix = Date.now();
    const displayName = `E2E Flat Plan ${uniqueSuffix}`;
    const code = `e2e-flat-${uniqueSuffix}`;

    await openSubscriptionSubPage(page, "Plans");

    await test.step("[Positive] page header and organization/search controls are visible", async () => {
      await expect(page).toHaveURL(/\/subscription\/plans$/);
      await expect(page.getByRole("heading", { name: "Subscription plans" })).toBeVisible();
      await expect(page.getByRole("combobox", { name: "Organization" })).toBeVisible();
      await expect(page.getByPlaceholder("Search plan name or code")).toBeVisible();
    });

    await test.step("[Positive] header actions include Discounts, Refresh and Create plan", async () => {
      await expect(page.getByRole("link", { name: "Discounts" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Refresh" })).toBeVisible();
      await expect(page.getByRole("link", { name: "Create plan" }).first()).toBeVisible();
    });

    await test.step("[Positive] empty catalogue shows 'No subscription plan yet'", async () => {
      const emptyState = page.getByText("No subscription plan yet", { exact: true });
      if (await emptyState.isVisible().catch(() => false)) {
        await expect(
          page.getByText("Create your first plan to start selling subscriptions."),
        ).toBeVisible();
      }
    });

    await test.step("[Positive] search narrows the plan catalogue", async () => {
      const search = page.getByPlaceholder("Search plan name or code");
      await search.fill("a-plan-code-that-should-not-exist-xyz");
      const noMatch = page.getByText("No plans match this search", { exact: true });
      const noneYet = page.getByText("No subscription plan yet", { exact: true });
      await expect(noMatch.or(noneYet)).toBeVisible();
      await search.fill("");
    });

    await test.step("[Positive] Create plan opens the wizard on step 1 (Identity) with progress steps visible", async () => {
      await page.getByRole("link", { name: "Create plan" }).first().click();
      await expect(page).toHaveURL(/\/subscription\/plans\/create$/);
      await expect(page.getByRole("heading", { name: "Create subscription plan" })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Identity" })).toBeVisible();
      await expect(page.getByText("Pricing model", { exact: true })).toBeVisible();
      await expect(page.getByText("Review", { exact: true })).toBeVisible();
    });

    await test.step("[Positive] Back is disabled on step 1", async () => {
      await expect(page.getByRole("button", { name: "Back", exact: true })).toBeDisabled();
    });

    await test.step("[Positive] filling Identity and clicking Next advances to Pricing model", async () => {
      await fillIdentityStep(page, `E2E Wizard Plan ${uniqueSuffix}`, `e2e-wizard-${uniqueSuffix}`);
      await expect(page.getByRole("heading", { name: "Pricing model" })).toBeVisible();
    });

    await test.step("[Positive] Back returns to Identity with the entered values preserved", async () => {
      await page.getByRole("button", { name: "Back", exact: true }).click();
      await expect(page.getByRole("heading", { name: "Identity" })).toBeVisible();
      await expect(page.getByLabel("Display name")).not.toHaveValue("");
    });

    await test.step("[Negative] submitting Review with a blank display name is rejected", async () => {
      await page.getByLabel("Display name").fill("");
      await page.getByRole("button", { name: "Next" }).click();
      await page.getByRole("button", { name: "Next" }).click();
      await page.getByRole("button", { name: "Next" }).click();
      await page.getByRole("button", { name: "Next" }).click();
      await expect(page.getByRole("heading", { name: "Review" })).toBeVisible();

      await page.getByRole("button", { name: /Create plan/ }).click();
      await expect(page.getByText("Check the highlighted fields", { exact: true })).toBeVisible();
      await expect(page).toHaveURL(/\/subscription\/plans\/create$/);
    });

    await test.step("[Positive] create a minimal flat-fee plan through the wizard", async () => {
      await openSubscriptionSubPage(page, "Plans");
      await page.getByRole("link", { name: "Create plan" }).first().click();
      await expect(page).toHaveURL(/\/subscription\/plans\/create$/);

      await fillIdentityStep(page, displayName, code);
      await fillPricingStep(page, "19.00");
      await skipUsageLimitsAndTrialSteps(page);

      await expect(page.getByRole("heading", { name: "Review" })).toBeVisible();
      await expect(page.getByText(displayName).first()).toBeVisible();

      await page.getByRole("button", { name: /Create plan/ }).click();
      await expect(page.getByText("Plan created", { exact: true })).toBeVisible({
        timeout: 20_000,
      });
    });

    await test.step("[Positive] lands on the plan detail page with the new plan's data", async () => {
      await expect(page).toHaveURL(/\/subscription\/plans\/[^/]+$/, { timeout: 20_000 });
      await expect(page.getByRole("heading", { name: displayName, level: 1 })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Prices" })).toBeVisible();
      await expect(page.getByText("USD", { exact: true }).first()).toBeVisible();
    });

    await test.step("[Positive] detail page exposes Duplicate plan and Edit, and no separate add-price entry", async () => {
      await expect(page.getByRole("link", { name: "Duplicate plan" })).toBeVisible();
      await expect(page.getByRole("link", { name: "Edit" })).toBeVisible();

      // Adding a price is part of editing the plan now. A second entry point existed and had its
      // own form, which could not author billing alignment, tax or an automatic discount at all.
      await expect(page.getByRole("link", { name: "Add price" })).toHaveCount(0);
    });

    await test.step("[Positive] a second price is added through the plan editor", async () => {
      await page.getByRole("link", { name: "Edit" }).click();
      await expect(page).toHaveURL(/\/edit$/);
      await expect(page.getByRole("heading", { name: `Edit ${displayName}` })).toBeVisible();

      await page.getByRole("button", { name: "Next" }).click();
      await expect(page.getByRole("heading", { name: "Pricing model" })).toBeVisible();

      // The plan's own prices are listed but not loaded into the form: editing adds prices, and
      // the ones already sold on are immutable.
      await expect(page.getByText("Already on this plan", { exact: true })).toBeVisible();

      await page.getByRole("button", { name: "Add another price" }).click();
      await page.getByPlaceholder("89.00").first().fill("29.00");
      await page.getByRole("button", { name: "Next" }).click();

      await skipUsageLimitsAndTrialSteps(page);
      await expect(page.getByRole("heading", { name: "Review" })).toBeVisible();

      await page.getByRole("button", { name: /Save changes/ }).click();
      await expect(page.getByText("Changes saved", { exact: true })).toBeVisible({
        timeout: 20_000,
      });
      await expect(page).toHaveURL(/\/subscription\/plans\/[^/]+$/);
      await expect(page.getByText("29.00", { exact: false }).first()).toBeVisible();
    });

    await test.step("[Positive] Duplicate plan pre-fills the wizard from this plan with a blank code", async () => {
      await page.getByRole("link", { name: "Duplicate plan" }).click();
      await expect(page).toHaveURL(/\/subscription\/plans\/create$/);
      await expect(page.getByRole("heading", { name: `Duplicate ${displayName}` })).toBeVisible();
      await expect(page.getByLabel("Display name")).toHaveValue(displayName);
      await expect(page.getByRole("textbox", { name: "Code", exact: true })).toHaveValue("");
      await page.goBack();
      await expect(page.getByRole("heading", { name: displayName, level: 1 })).toBeVisible();
    });

    await test.step("[Positive] Edit opens the builder with identity fields locked", async () => {
      await page.getByRole("link", { name: "Edit" }).click();
      await expect(page).toHaveURL(/\/edit$/);
      await expect(
        page.getByRole("heading", { name: `Edit ${displayName}`, level: 1 }),
      ).toBeVisible();
      await expect(page.getByRole("heading", { name: "Identity" })).toBeVisible();
      await expect(page.getByRole("textbox", { name: "Code", exact: true })).toBeDisabled();
    });

    await test.step("[Positive] editing the description and saving returns to the detail page", async () => {
      const description = `Updated by e2e at ${uniqueSuffix}`;
      await page.getByLabel("Description").fill(description);

      // Advance through the remaining steps to Review, then save.
      await page.getByRole("button", { name: "Next" }).click();
      await expect(page.getByRole("heading", { name: "Pricing model" })).toBeVisible();
      await page.getByRole("button", { name: "Next" }).click();
      await expect(page.getByRole("heading", { name: "What the plan grants" })).toBeVisible();
      await page.getByRole("button", { name: "Next" }).click();
      await expect(page.getByRole("heading", { name: "Trial" })).toBeVisible();
      await page.getByRole("button", { name: "Next" }).click();
      await expect(page.getByRole("heading", { name: "Review" })).toBeVisible();

      await page.getByRole("button", { name: /Save changes|Save/ }).click();
      await expect(page).toHaveURL(/\/subscription\/plans\/[^/]+$/, { timeout: 20_000 });
      await expect(page.getByRole("heading", { name: displayName, level: 1 })).toBeVisible();
      await expect(page.getByText(description)).toBeVisible();
    });

    await test.step("[Positive] the plan is discoverable from the plan list by its code", async () => {
      const listPath = page.url().replace(/\/subscription\/plans\/[^/?]+.*/, "/subscription/plans");
      await page.goto(listPath);
      await page.getByPlaceholder("Search plan name or code").fill(code);
      await expect(page.getByText(displayName).first()).toBeVisible();
    });

    await test.step("[Positive] Discounts navigates to the discounts page", async () => {
      await page.getByRole("link", { name: "Discounts" }).click();
      await expect(page).toHaveURL(/\/subscription\/discounts$/);
      await expect(page.getByRole("heading", { name: "Subscription discounts" })).toBeVisible();
      await expect(page.getByText("Discount catalogue", { exact: true })).toBeVisible();
    });
  });
});
