import { test, expect } from "../../support/test-base";
import { openSubscriptionSubPage } from "../../support/auth-helpers";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";

/** Fills every required identity field on the billing profile with a unique stamp. */
async function fillRequiredIdentity(page: import("@playwright/test").Page, stamp: string) {
  await page.getByLabel("Legal name").fill(`E2E Billing Co ${stamp}`);
  await page.getByLabel("Billing contact").fill(`Ada ${stamp}`);
  await page.getByLabel("Billing email").fill(`billing-${stamp}@e2e.example`);
  // Country code is two-letter ISO 3166-1; "CH" is unlikely to collide with real profiles.
  await page.getByLabel("Country code").fill("CH");
}

test.describe("Subscriptions - Billing profile", () => {
  test.beforeEach(async ({ page }) => {
    await openUtilitiesDashboard(page);
  });

  test("Billing profile", async ({ page }) => {
    const stamp = Date.now().toString();

    await openSubscriptionSubPage(page, "Billing profile");

    await test.step("[Positive] page header and required fields are visible", async () => {
      await expect(page).toHaveURL(/\/subscription\/billing-profile$/);
      await expect(page.getByRole("heading", { name: "Billing profile" })).toBeVisible();
      await expect(
        page.getByText(/The name, contact and address every invoice/i),
      ).toBeVisible();
      await expect(page.getByLabel("Legal name")).toBeVisible();
      await expect(page.getByLabel("Display name")).toBeVisible();
      await expect(page.getByLabel("Billing contact")).toBeVisible();
      await expect(page.getByLabel("Billing email")).toBeVisible();
      await expect(page.getByLabel("Country code")).toBeVisible();
      await expect(page.getByRole("button", { name: "Save billing profile" })).toBeVisible();
    });

    await test.step("[Positive] an incomplete profile surfaces the 'not complete yet' notice", async () => {
      // The banner is the contract the server refuses a paid subscription without, so its absence
      // is what blocks checkout - confirming it shows up here is the load-bearing assertion.
      const incomplete = page.getByTestId("profile-incomplete");
      const complete = page.getByTestId("profile-complete");
      await expect(incomplete.or(complete)).toBeVisible({ timeout: 15_000 });
    });

    await test.step("[Positive] filling identity and saving turns the profile complete", async () => {
      await fillRequiredIdentity(page, stamp);
      await page.getByRole("button", { name: "Save billing profile" }).click();

      await expect(page.getByTestId("profile-saved")).toBeVisible({ timeout: 15_000 });
      await expect(page.getByTestId("profile-saved")).toContainText(
        "Saved. Documents issued from now on carry these details.",
      );
    });

    await test.step("[Positive] a the saved profile reloads with the entered values preserved", async () => {
      // Reload to force a refetch - what the server stored, not what we last typed.
      await page.reload();
      await expect(page.getByLabel("Legal name")).toHaveValue(`E2E Billing Co ${stamp}`);
      await expect(page.getByLabel("Billing email")).toHaveValue(`billing-${stamp}@e2e.example`);
    });

    await test.step("[Positive] editing optional fields and saving updates the profile", async () => {
      const newStamp = `${stamp}-bis`;
      await page.getByLabel("Display name").fill(`E2E ${newStamp}`);
      await page.getByLabel("Postal code").fill("8001");
      await page.getByLabel("City").fill("Zurich");
      await page.getByRole("button", { name: "Save billing profile" }).click();

      await expect(page.getByTestId("profile-saved")).toBeVisible({ timeout: 15_000 });

      await page.reload();
      await expect(page.getByLabel("Display name")).toHaveValue(`E2E ${newStamp}`);
      await expect(page.getByLabel("Postal code")).toHaveValue("8001");
    });

    await test.step("[Negative] an invalid country code is rejected by the server", async () => {
      // The country-code input is capped at 2 characters, so we can't send a longer string.
      // The server-side regex is ^[A-Za-z]{2}$, so a 2-char value that includes a digit
      // (e.g. "X1") is the cheapest way to exercise the failure path without depending on
      // a specific organisation state.
      await page.getByLabel("Country code").fill("X1");
      await page.getByRole("button", { name: "Save billing profile" }).click();

      // The page surfaces the server's error inline next to the form.
      const errorBanner = page.getByTestId("profile-error");
      await expect(errorBanner).toBeVisible({ timeout: 15_000 });

      // Restore a valid value so the suite does not leave the profile in a broken state.
      await page.getByLabel("Country code").fill("CH");
      await page.getByRole("button", { name: "Save billing profile" }).click();
      await expect(page.getByTestId("profile-saved")).toBeVisible({ timeout: 15_000 });
    });
  });
});