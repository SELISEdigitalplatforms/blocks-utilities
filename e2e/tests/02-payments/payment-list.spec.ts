import { test, expect } from "../../support/test-base";
import { openPaymentsSubPage } from "../../support/auth-helpers";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";

test.describe("Payments", () => {
  test.beforeEach(async ({ page }) => {
    await openUtilitiesDashboard(page);
  });

  test("Payment List", async ({ page }) => {
    await openPaymentsSubPage(page, "Payment List");

    await test.step("[Positive] header shows the live status badge and filters", async () => {
      await expect(page.getByRole("heading", { name: "Payment list" })).toBeVisible();
      await expect(page.getByText("Live", { exact: true })).toBeVisible();
      await expect(page.getByText("Filter payments", { exact: true })).toBeVisible();
    });

    await test.step("[Positive] filter fields cover provider/status/currency/flow/organization", async () => {
      await expect(page.getByText("All providers", { exact: true })).toBeVisible();
      await expect(page.getByText("All statuses", { exact: true })).toBeVisible();
      await expect(page.getByRole("combobox").filter({ hasText: "All currencies" })).toBeVisible();
      await expect(page.getByRole("combobox").filter({ hasText: "All flows" })).toBeVisible();
      await expect(
        page.getByRole("combobox").filter({ hasText: "All organizations" }),
      ).toBeVisible();
    });

    await test.step("[Positive] Apply filters can be triggered without error", async () => {
      await page.getByRole("button", { name: "Apply filters" }).click();
      await expect(page.getByRole("heading", { name: "Payment list" })).toBeVisible();
    });

    await test.step("[Positive] empty environment shows 'No payments yet'", async () => {
      const emptyState = page.getByText("No payments yet", { exact: true });
      if (await emptyState.isVisible().catch(() => false)) {
        await expect(
          page.getByText("Payments will appear here as soon as they are created."),
        ).toBeVisible();
      }
    });
  });
});
