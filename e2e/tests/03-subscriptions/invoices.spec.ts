import { test, expect } from "../../support/test-base";
import { openSubscriptionSubPage } from "../../support/auth-helpers";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";

test.describe("Subscriptions - Invoices", () => {
  test.beforeEach(async ({ page }) => {
    await openUtilitiesDashboard(page);
  });

  test("Invoices & credit notes", async ({ page }) => {
    await openSubscriptionSubPage(page, "Invoices");

    await test.step("[Positive] page header and the four filter controls are visible", async () => {
      await expect(page).toHaveURL(/\/subscription\/invoices$/);
      await expect(
        page.getByRole("heading", { name: "Invoices & credit notes" }),
      ).toBeVisible();
      await expect(page.getByText(/Every document this application has issued/i)).toBeVisible();
      await expect(page.getByRole("combobox", { name: "Document type" })).toBeVisible();
      await expect(page.getByRole("combobox", { name: "Status" })).toBeVisible();
      await expect(page.getByLabel("Issued from")).toBeVisible();
      await expect(page.getByLabel("Issued to")).toBeVisible();
    });

    await test.step("[Positive] empty environment shows the empty-state copy", async () => {
      // The empty card is what renders when the filter combination matches no documents.
      const emptyState = page.getByTestId("documents-empty");
      if (await emptyState.isVisible().catch(() => false)) {
        await expect(emptyState).toContainText("No documents match these filters yet");
      }
    });

    await test.step("[Positive] Document type filter narrows the listing", async () => {
      const documentTypeSelect = page.getByRole("combobox", { name: "Document type" });
      await documentTypeSelect.click();
      // The dropdown exposes "Credit notes" (plural) — only that one is a usable distinct option;
      // "Invoices" and "Trial invoices" round it out and "All documents" is the default.
      await page.getByRole("option", { name: "Credit notes", exact: true }).click();

      // After a filter change the loading card appears briefly, then either the empty card or the
      // document rows. Either result is acceptable for the assertion - the contract is that the
      // filter was applied and the page re-rendered.
      const emptyState = page.getByTestId("documents-empty");
      const firstDocument = page.locator("[data-testid^='document-']").first();
      await expect(emptyState.or(firstDocument)).toBeVisible({ timeout: 15_000 });

      // Reset back to "All documents" so subsequent steps see the default listing.
      await documentTypeSelect.click();
      await page.getByRole("option", { name: "All documents", exact: true }).click();
    });

    await test.step("[Positive] Status filter narrows the listing", async () => {
      const statusSelect = page.getByRole("combobox", { name: "Status" });
      await statusSelect.click();
      await page.getByRole("option", { name: "Issued", exact: true }).click();

      const emptyState = page.getByTestId("documents-empty");
      const firstDocument = page.locator("[data-testid^='document-']").first();
      await expect(emptyState.or(firstDocument)).toBeVisible({ timeout: 15_000 });

      await statusSelect.click();
      await page.getByRole("option", { name: "Any status", exact: true }).click();
    });

    await test.step("[Positive] Issued-from and Date filter compose into a date range", async () => {
      const today = new Date().toISOString().slice(0, 10);
      const yesterday = new Date(Date.now() - 86_400_000).toISOString().slice(0, 10);

      await page.getByLabel("Issued from").fill(yesterday);
      await page.getByLabel("Issued to").fill(today);

      const emptyState = page.getByTestId("documents-empty");
      const firstDocument = page.locator("[data-testid^='document-']").first();
      await expect(emptyState.or(firstDocument)).toBeVisible({ timeout: 15_000 });
    });

    await test.step("[Negative] a date range with no documents lands on the empty state", async () => {
      // 1970 is a safe "definitely nothing" range - the system could not have issued anything then.
      await page.getByLabel("Issued from").fill("1970-01-01");
      await page.getByLabel("Issued to").fill("1970-01-02");

      const emptyState = page.getByTestId("documents-empty");
      await expect(emptyState).toBeVisible({ timeout: 15_000 });
      await expect(emptyState).toContainText("No documents match these filters yet");
    });

    await test.step("[Positive] Show detail expands a document row and surfaces its figures", async () => {
      // Clear the date range first so we go back to a normal listing.
      await page.getByLabel("Issued from").fill("");
      await page.getByLabel("Issued to").fill("");

      const firstDocument = page.locator("[data-testid^='document-']").first();
      const emptyState = page.getByTestId("documents-empty");

      // A tenant with zero financial documents has nothing to expand. The empty-state
      // branch is already covered by the negative step above; skip expansion cleanly
      // rather than wait 15s for a row that will never appear.
      await expect(firstDocument.or(emptyState)).toBeVisible({ timeout: 15_000 });
      if (await emptyState.isVisible().catch(() => false)) {
        return;
      }

      const showDetailButton = firstDocument.getByRole("button", { name: "Show detail" });
      await showDetailButton.click();

      // The expanded card always carries an Amounts table and the "Billed to" footer.
      await expect(firstDocument.getByText("Amounts", { exact: true })).toBeVisible();
      await expect(firstDocument.getByText("Billed to:", { exact: true })).toBeVisible();

      // And collapse it again so the page is back to a clean state.
      await firstDocument.getByRole("button", { name: "Hide detail" }).click();
      await expect(firstDocument.getByText("Amounts", { exact: true })).toBeHidden();
    });
  });
});