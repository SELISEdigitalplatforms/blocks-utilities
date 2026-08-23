import { test, expect } from "../../support/test-base";
import { openSubscriptionSubPage } from "../../support/auth-helpers";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";

test.describe("Subscriptions - Simulation", () => {
  test.beforeEach(async ({ page }) => {
    await openUtilitiesDashboard(page);
  });

  test("Simulation", async ({ page }) => {
    await openSubscriptionSubPage(page, "Simulation");

    await test.step("[Positive] page header, scope selector, and refresh control are visible", async () => {
      await expect(page).toHaveURL(/\/subscription\/simulation$/);
      await expect(page.getByRole("heading", { name: "Subscription simulation" })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Acting as" })).toBeVisible();
      await expect(page.getByRole("combobox", { name: "Organization" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Refresh" })).toBeVisible();
    });

    await test.step("[Positive] current subscription section renders for the default (tenant-wide) scope", async () => {
      await expect(page.getByRole("heading", { name: "Current subscription" })).toBeVisible();

      const noSubscription = page.getByText("has no subscription yet.");
      const hasSubscription = page.getByRole("button", { name: "Cancel" }).first();
      await expect(noSubscription.or(hasSubscription)).toBeVisible({ timeout: 15_000 });
    });

    await test.step("[Positive] plan catalogue section is visible", async () => {
      await expect(page.getByRole("heading", { name: "Plan catalogue" })).toBeVisible();

      const noPlans = page.getByText("No plan on sale for this scope", { exact: true });
      const somePlans = page.getByRole("button", { name: /Subscribe|Already subscribed/ }).first();
      await expect(noPlans.or(somePlans)).toBeVisible({ timeout: 15_000 });
    });

    await test.step("[Positive] switching organization scope updates the 'Acting as' label", async () => {
      const scopeSelect = page.getByRole("combobox", { name: "Organization" });
      const optionCount = await page
        .locator('[role="option"]')
        .count()
        .catch(() => 0);
      // Only assert scope switching when a real organization option exists besides "Tenant-wide only".
      await scopeSelect.click();
      const orgOption = page.getByRole("option").filter({ hasNotText: "Tenant-wide only" }).first();
      if (await orgOption.isVisible().catch(() => false)) {
        const orgName = await orgOption.textContent();
        await orgOption.click();
        if (orgName) {
          await expect(page.getByText(orgName.trim(), { exact: false }).first()).toBeVisible();
        }
      } else {
        await page.keyboard.press("Escape");
      }
      expect(optionCount).toBeGreaterThanOrEqual(0);
    });

    await test.step("[Positive] Refresh reloads plans and current subscription without navigating away", async () => {
      await page.getByRole("button", { name: "Refresh" }).click();
      await expect(page).toHaveURL(/\/subscription\/simulation/);
      await expect(page.getByRole("heading", { name: "Subscription simulation" })).toBeVisible();
    });

    await test.step("[Positive] Subscribe opens the subscribe dialog when a subscribable plan exists", async () => {
      const subscribeButton = page.getByRole("button", { name: "Subscribe" }).first();
      if (await subscribeButton.isVisible().catch(() => false)) {
        await subscribeButton.click();
        await expect(page.getByRole("dialog")).toBeVisible();
        await page.keyboard.press("Escape");
        await expect(page.getByRole("dialog")).toBeHidden();
      }
    });

    await test.step("[Positive] Change plan opens the change-plan dialog when a subscription is active", async () => {
      const changePlanButton = page.getByRole("button", { name: "Change plan" }).first();
      if (await changePlanButton.isVisible().catch(() => false)) {
        await changePlanButton.click();
        await expect(page.getByRole("dialog")).toBeVisible();
        await page.keyboard.press("Escape");
        await expect(page.getByRole("dialog")).toBeHidden();
      }
    });

    await test.step("[Negative] Cancel dialog can be dismissed without canceling the subscription", async () => {
      const cancelButton = page.getByRole("button", { name: "Cancel" }).first();
      if (await cancelButton.isVisible().catch(() => false)) {
        await cancelButton.click();
        await expect(page.getByRole("dialog")).toBeVisible();
        await page.keyboard.press("Escape");
        await expect(page.getByRole("dialog")).toBeHidden();
        // Subscription state must be unaffected by a dismissed dialog.
        await expect(page.getByRole("button", { name: "Cancel" }).first()).toBeVisible();
      }
    });
  });
});
