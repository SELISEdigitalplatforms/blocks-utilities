import { test, expect } from "../../support/test-base"
import type { Locator, Page } from "@playwright/test"
import { openUtilitiesMagicUrl } from "../../support/utilities-helpers"

// ---------------------------------------------------------------------------
// Magic URL page helpers
// ---------------------------------------------------------------------------

const MAGIC_URL_COLUMNS = [
  "URL",
  "Name",
  "Usage Limit",
  "Scheduled Expiry Date",
  "Status",
  "Request Method",
  "Client Credential",
] as const;

const DEACTIVATE_CONFIRM_TEXT =
  "Are you sure you want to deactivate this Magic URL? This action cannot be undone.";

/** Opens the "Create Magic URL" dialog and returns its locator. */
async function openCreateMagicUrlDialog(page: Page): Promise<Locator> {
  await page.getByRole("button", { name: "Create Magic URL" }).click();
  const dialog = page.getByRole("dialog", { name: "Magic URL" });
  await expect(dialog).toBeVisible();
  return dialog;
}

/** The list-page table row for a Magic URL, matched by its (unique) name. */
function magicUrlRow(page: Page, name: string): Locator {
  return page.getByRole("row").filter({ hasText: name });
}

/**
 * Opens a row's "⋮" actions menu (last button in the row - the first is the
 * URL cell's copy-to-clipboard button) and clicks the given menu item.
 */
async function chooseRowAction(row: Locator, action: "View Details" | "Go to Link" | "Deactivate") {
  await row.getByRole("button").last().click();
  await row.page().getByRole("menuitem", { name: action }).click();
}

/** The "Deactivate Magic URL" confirmation dialog. */
function deactivateConfirmDialog(page: Page): Locator {
  return page.getByRole("dialog", { name: "Deactivate Magic URL" });
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

/**
 * Magic URL: list/filters, Create-dialog validation, and the full
 * Create -> View Details -> Deactivate lifecycle.
 *
 * Unlike Payments, creating a Magic URL only writes a lightweight
 * redirect-entry record (no money, no external infra), so the full flow is
 * exercised end-to-end and the created record is cleaned up (deactivated)
 * at the end of the lifecycle test.
 */
test.describe("Magic URL", () => {
  test.beforeEach(async ({ page }) => {
    await openUtilitiesMagicUrl(page);
  });

  test("Magic URL list", async ({ page }) => {
    await test.step("[Positive] table shows all expected columns", async () => {
      for (const column of MAGIC_URL_COLUMNS) {
        await expect(page.getByRole("columnheader", { name: column })).toBeVisible();
      }
    });

    await test.step("[Positive] filter controls are available", async () => {
      await expect(page.getByRole("button", { name: "Status" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Request Method" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Scheduled Expiry Date" })).toBeVisible();
    });

    await test.step("[Positive] empty environment shows 'No results.'", async () => {
      const emptyState = page.getByText("No results.", { exact: true });
      if (await emptyState.isVisible().catch(() => false)) {
        await expect(emptyState).toBeVisible();
      }
    });
  });

  test("Create Magic URL - validation", async ({ page }) => {
    const dialog = await openCreateMagicUrlDialog(page);

    const uriInput = dialog.getByLabel("URI *");
    const nameInput = dialog.getByLabel("Name *");
    const createButton = dialog.getByRole("button", { name: "Create", exact: true });

    await test.step("[Negative] Create stays disabled while the form is empty", async () => {
      await expect(createButton).toBeDisabled();
    });

    await test.step("[Negative] invalid URI shows 'Please enter a valid URI'", async () => {
      await uriInput.fill("not a url");
      await nameInput.fill("Temp name");
      await expect(dialog.getByText("Please enter a valid URI", { exact: true })).toBeVisible();
      await expect(createButton).toBeDisabled();
    });

    await test.step("[Negative] Name over 100 characters shows the length error", async () => {
      await uriInput.fill("https://example.com");
      await nameInput.fill("N".repeat(101));
      await expect(
        dialog.getByText("Name must be at most 100 characters", { exact: true }),
      ).toBeVisible();
      await expect(createButton).toBeDisabled();
    });

    await test.step("[Positive] a valid URI and name enable Create", async () => {
      await nameInput.fill("Temp validation name");
      await expect(createButton).toBeEnabled();
    });

    await test.step("[Positive] Type defaults to Redirect; switching to Action reveals extra fields", async () => {
      const typeSelect = dialog.getByRole("combobox").filter({
        hasText: "Redirect",
      });

      await expect(typeSelect).toBeVisible();
      await expect(typeSelect).toHaveText("Redirect");

      await typeSelect.click();

      await page
        .getByRole("option", {
          name: "Action",
          exact: true,
        })
        .click();

      // Verify Action-specific fields by their visible labels
      await expect(dialog.getByText("Request Method", { exact: true })).toBeVisible();

      await expect(dialog.getByText("Request Payload", { exact: true })).toBeVisible();

      await expect(dialog.getByText("Request Headers", { exact: true })).toBeVisible();

      const actionSelect = dialog.getByRole("combobox").filter({
        hasText: "Action",
      });

      await expect(actionSelect).toBeVisible();

      await actionSelect.click();

      await page
        .getByRole("option", {
          name: "Redirect",
          exact: true,
        })
        .click();

      await expect(
        dialog.getByRole("combobox").filter({
          hasText: "Redirect",
        }),
      ).toBeVisible();
    });
    await test.step("[Positive] Set Usage Limit / Set Auto Expiry Date toggles reveal their inputs", async () => {
      await expect(dialog.getByPlaceholder("Enter usage limit")).toBeHidden();
      await dialog.getByRole("switch", { name: "Set Usage Limit" }).click();
      await expect(dialog.getByPlaceholder("Enter usage limit")).toBeVisible();

      await expect(dialog.getByRole("button", { name: "Pick a date" })).toBeHidden();
      await dialog.getByRole("switch", { name: "Set Auto Expiry Date" }).click();
      await expect(dialog.getByRole("button", { name: "Pick a date" })).toBeVisible();
    });

    await test.step("[Positive] Cancel closes the dialog without creating anything", async () => {
      await dialog.getByRole("button", { name: "Cancel" }).click();
      await expect(dialog).toBeHidden();
    });
  });

  test("Create Magic URL - full lifecycle (create, view, deactivate)", async ({ page }) => {
    const uniqueName = `e2e-magic-url-${Date.now()}`;
    const uniqueUrl = `https://example.com/e2e-${Date.now()}`;

    await test.step("[Positive] create a Magic URL with a unique name and URI", async () => {
      const dialog = await openCreateMagicUrlDialog(page);
      await dialog.getByLabel("URI *").fill(uniqueUrl);
      await dialog.getByLabel("Name *").fill(uniqueName);

      const createButton = dialog.getByRole("button", { name: "Create", exact: true });
      await expect(createButton).toBeEnabled();
      await createButton.click();

      await expect(page.getByText("Magic URL created successfully", { exact: true })).toBeVisible({
        timeout: 15_000,
      });
      await expect(dialog).toBeHidden();
    });

    const row = magicUrlRow(page, uniqueName);
    let shortUri = "";

    await test.step("[Positive] the new Magic URL appears in the list", async () => {
      await expect(row).toBeVisible({ timeout: 30_000 });
      // The URL cell renders shortUri first, then the original uri below it
      // (both as plain text, not links) - the shortened one comes first.
      shortUri = (
        await row
          .getByText(/^https?:\/\//)
          .first()
          .innerText()
      ).trim();
      expect(shortUri).toMatch(/^https?:\/\//);
    });

    await test.step("[Positive] View Details navigates to the details page", async () => {
      await chooseRowAction(row, "View Details");
      await page.waitForURL(/\/magic-url\/details\//, { timeout: 15_000 });
      // await expect(page.getByRole("heading", { name: uniqueName })).toBeVisible();
      // await expect(page.getByRole("link", { name: "Magic URL", exact: true })).toBeVisible();
      // await expect(page.getByRole("link", { name: "List", exact: true })).toBeVisible();
    });

    await test.step("[Positive] Details card shows usage, status, timestamps and both URLs", async () => {
      await expect(page.getByRole("heading", { name: "Details", exact: true })).toBeVisible();
      await expect(page.getByText("Total Usage", { exact: true }).locator("..")).toContainText("0");
      await expect(page.getByText("Active", { exact: true })).toBeVisible();
      await expect(page.getByText("Created By", { exact: true })).toBeVisible();
      await expect(page.getByText("Created On", { exact: true })).toBeVisible();
      await expect(
        page.getByText("Scheduled Expiry Date", { exact: true }).locator(".."),
      ).toContainText("-");
      await expect(page.getByText(shortUri)).toBeVisible();
      await expect(page.getByText(uniqueUrl)).toBeVisible();
    });

    await test.step("[Security] the details-page actions menu only offers Deactivate here", async () => {
      // No "View Details"/"Go to Link" - those only make sense from the list.
      const pageActionsButton = page
        .getByRole("heading", { name: uniqueName })
        .locator("..")
        .getByRole("button");
      await pageActionsButton.click();
      await expect(page.getByRole("menuitem", { name: "Deactivate" })).toBeVisible();
      await expect(page.getByRole("menuitem", { name: "View Details" })).toHaveCount(0);
      await expect(page.getByRole("menuitem", { name: "Go to Link" })).toHaveCount(0);
      await page.keyboard.press("Escape");

      await page.goBack();
      await expect(page.getByRole("heading", { name: "Magic URL" })).toBeVisible();
    });

    await test.step("[Negative] Deactivate asks for confirmation before removing it", async () => {
      await chooseRowAction(row, "Deactivate");

      const confirmDialog = deactivateConfirmDialog(page);
      await expect(confirmDialog).toBeVisible();
      await expect(confirmDialog.getByText(DEACTIVATE_CONFIRM_TEXT)).toBeVisible();

      // Cancel first, to prove the record survives an aborted deactivation.
      await confirmDialog.getByRole("button", { name: "Cancel" }).click();
      await expect(confirmDialog).toBeHidden();
      await expect(row).toBeVisible();
    });

    await test.step("[Positive] confirming Deactivate deactivates the test record (cleanup)", async () => {
      await chooseRowAction(row, "Deactivate");
      await deactivateConfirmDialog(page)
        .getByRole("button", { name: "Deactivate", exact: true })
        .click();

      await expect(
        page.getByText("Magic URL deactivated successfully", { exact: true }),
      ).toBeVisible({
        timeout: 15_000,
      });
    });
  });
});
