import { test, expect } from "../../support/test-base";
import { openSubscriptionSubPage } from "../../support/auth-helpers";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";

test.describe("Subscriptions - Merchant profile", () => {
  test.beforeEach(async ({ page }) => {
    await openUtilitiesDashboard(page);
  });

  test("Merchant profile", async ({ page }) => {
    const stamp = Date.now().toString();

    await openSubscriptionSubPage(page, "Merchant profile");

    await test.step("[Positive] page header, identity fields, branding controls are visible", async () => {
      await expect(page).toHaveURL(/\/subscription\/merchant-profile$/);
      await expect(page.getByRole("heading", { name: "Merchant profile" })).toBeVisible();
      await expect(
        page.getByText(/The legal identity this tenant issues its invoices/i),
      ).toBeVisible();
      await expect(page.getByLabel("Legal name")).toBeVisible();
      await expect(page.getByLabel("Trading name")).toBeVisible();
      await expect(page.getByLabel("Support email")).toBeVisible();
      await expect(page.getByLabel("Payment instructions")).toBeVisible();
      await expect(page.getByRole("heading", { name: "Invoice branding" })).toBeVisible();
      await expect(page.getByLabel("Primary color")).toBeVisible();
      await expect(page.getByLabel("Accent color")).toBeVisible();
      await expect(page.getByRole("button", { name: "Save merchant profile" })).toBeVisible();
    });

    await test.step("[Positive] inherited vs own identity is reported on the page", async () => {
      // Both banners carry the same data contract: one or the other is shown depending on whether
      // the tenant has set its own identity. Either is acceptable here - the assertion is about
      // the page not falling into an unexplained state.
      const inherited = page.getByTestId("merchant-inherited");
      const own = page.getByTestId("merchant-own");
      await expect(inherited.or(own)).toBeVisible({ timeout: 15_000 });
    });

    await test.step("[Positive] editing identity, support email and saving updates the profile", async () => {
      await page.getByLabel("Legal name").fill(`E2E Merchant ${stamp}`);
      await page.getByLabel("Trading name").fill(`E2E ${stamp}`);
      await page.getByLabel("Support email").fill(`support-${stamp}@e2e.example`);
      await page.getByLabel("Payment instructions").fill(`Bank: E2E Bank\nRef: ${stamp}`);

      await page.getByRole("button", { name: "Save merchant profile" }).click();

      await expect(page.getByTestId("merchant-saved")).toBeVisible({ timeout: 15_000 });
      await expect(page.getByTestId("merchant-saved")).toContainText(
        "Saved. Documents issued from now on name this seller.",
      );
    });

    await test.step("[Positive] a reload re-fetches the saved profile with values preserved", async () => {
      await page.reload();
      await expect(page.getByLabel("Legal name")).toHaveValue(`E2E Merchant ${stamp}`);
      await expect(page.getByLabel("Trading name")).toHaveValue(`E2E ${stamp}`);
      await expect(page.getByLabel("Support email")).toHaveValue(`support-${stamp}@e2e.example`);
      await expect(page.getByLabel("Payment instructions")).toContainText(`Ref: ${stamp}`);
    });

    await test.step("[Positive] invoice branding colors can be edited and saved", async () => {
      // Color inputs accept a 7-character #RRGGBB value; pick something distinctive so a no-op save
      // is impossible to confuse with a real change.
      const newPrimary = "#0F4C81";
      const newAccent = "#F2E8CF";

      // The branded text input next to the color picker is the one that round-trips server-side.
      await page.getByLabel("Primary color").fill(newPrimary);
      await page.getByLabel("Accent color").fill(newAccent);

      await page.getByRole("button", { name: "Save merchant profile" }).click();
      await expect(page.getByTestId("merchant-saved")).toBeVisible({ timeout: 15_000 });

      // The server returns hex lowercased - the picker normalises to uppercase on pick but the
      // stored value comes back lowercased, so compare case-insensitively.
      const expectedPrimary = newPrimary.toLowerCase();
      const expectedAccent = newAccent.toLowerCase();

      await page.reload();
      await expect(page.getByLabel("Primary color")).toHaveValue(expectedPrimary);
      await expect(page.getByLabel("Accent color")).toHaveValue(expectedAccent);
    });
  });
});