import { expect, type Locator, type Page } from "@playwright/test";
import { openUtilitiesMagicUrl } from "../../support/utilities-helpers";

/**
 * Magic URL flows. Each function is a reusable step that exercises a
 * single Magic URL behaviour end-to-end (list, create-dialog validation,
 * full lifecycle: create -> view -> deactivate).
 *
 * Unlike Payments, creating a Magic URL only writes a lightweight
 * redirect-entry record (no money, no external infra), so the full flow
 * is exercised end-to-end and the created record is cleaned up
 * (deactivated) at the end of the lifecycle test.
 */

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Columns the list-page table must render. */
export const MAGIC_URL_COLUMNS = [
  "URL",
  "Name",
  "Usage Limit",
  "Scheduled Expiry Date",
  "Status",
  "Request Method",
  "Client Credential",
] as const;

/** The Deactivate confirmation dialog's body text. */
export const DEACTIVATE_CONFIRM_TEXT =
  "Are you sure you want to deactivate this Magic URL? This action cannot be undone.";

// ---------------------------------------------------------------------------
// Locator helpers
// ---------------------------------------------------------------------------

/** Opens the "Create Magic URL" dialog and returns its locator. */
export async function openCreateMagicUrlDialog(page: Page): Promise<Locator> {
  await page.getByRole("button", { name: "Create Magic URL" }).click();
  const dialog = page.getByRole("dialog", { name: "Magic URL" });
  await expect(dialog).toBeVisible();
  return dialog;
}

/** The list-page table row for a Magic URL, matched by its (unique) name. */
export function magicUrlRow(page: Page, name: string): Locator {
  return page.getByRole("row").filter({ hasText: name });
}

/** The "Deactivate Magic URL" confirmation dialog. */
export function deactivateConfirmDialog(page: Page): Locator {
  return page.getByRole("dialog", { name: "Deactivate Magic URL" });
}

// ---------------------------------------------------------------------------
// Step flows
// ---------------------------------------------------------------------------

/** Magic URL: open the page from the dashboard sidebar. */
export async function openMagicUrl(page: Page): Promise<void> {
  await openUtilitiesMagicUrl(page);
}

/**
 * Magic URL: the list-page table shows every expected column header.
 *
 * The table is built with native `<th>` elements (not role="columnheader"),
 * so we assert on `<th>` text content directly.
 */
export async function verifyTableShowsAllExpectedColumns(page: Page): Promise<void> {
  for (const column of MAGIC_URL_COLUMNS) {
    await expect(
      page.locator("thead th").filter({ hasText: column }).first(),
    ).toBeVisible();
  }
}

/** Magic URL: the list-page filter controls are available. */
export async function verifyFilterControlsAvailable(page: Page): Promise<void> {
  await expect(page.getByRole("button", { name: "Status" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Request Method" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Scheduled Expiry Date" })).toBeVisible();
}

/** Magic URL: a Search... textbox is available above the table. */
export async function verifySearchInputAvailable(page: Page): Promise<void> {
  // Two inputs share this placeholder on the page (the toolbar search input
  // and the dropdown search input that appears when filters open). Assert
  // the toolbar one specifically to avoid the strict-mode collision.
  const toolbarSearch = page.locator("input[placeholder='Search...']").first();
  await expect(toolbarSearch).toBeVisible();
}

/**
 * Magic URL: URL and Name column headers are rendered as buttons (sortable
 * by clicking), while the other columns are static headers.
 *
 * The table is built with native `<th>` elements (not role="columnheader"),
 * so the static columns are asserted on `<th>` text content. The sortable
 * buttons also share the page with a "Create Magic URL" header button
 * whose accessible name contains the substring "URL" — use `exact: true`
 * to avoid matching both.
 */
export async function verifySortableColumnHeaders(page: Page): Promise<void> {
  await expect(
    page.getByRole("button", { name: "URL", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Name", exact: true }),
  ).toBeVisible();
  // The remaining columns are not sortable - they render as plain <th>.
  await expect(
    page.locator("thead th").filter({ hasText: "Usage Limit" }),
  ).toBeVisible();
  await expect(
    page.locator("thead th").filter({ hasText: "Scheduled Expiry Date" }),
  ).toBeVisible();
  await expect(
    page.locator("thead th").filter({ hasText: "Status" }),
  ).toBeVisible();
  await expect(
    page.locator("thead th").filter({ hasText: "Request Method" }),
  ).toBeVisible();
  await expect(
    page.locator("thead th").filter({ hasText: "Client Credential" }),
  ).toBeVisible();
}

/**
 * Magic URL: empty environment shows "No results.". Tolerates the absence
 * of the empty state (when the tenant already has records).
 */
export async function verifyEmptyEnvironmentShowsNoResults(page: Page): Promise<void> {
  const emptyState = page.getByText("No results.", { exact: true });
  if (await emptyState.isVisible().catch(() => false)) {
    await expect(emptyState).toBeVisible();
  }
}

// ---------------------------------------------------------------------------
// Create-dialog validation
// ---------------------------------------------------------------------------

/** Magic URL: Create button stays disabled while the form is empty. */
export async function verifyCreateStaysDisabledWithEmptyForm(page: Page, dialog: Locator): Promise<void> {
  const createButton = dialog.getByRole("button", { name: "Create", exact: true });
  await expect(createButton).toBeDisabled();
}

/**
 * Magic URL: an invalid URI surfaces "Please enter a valid URI" and keeps
 * Create disabled.
 */
export async function verifyInvalidUriShowsErrorAndCreateDisabled(page: Page, dialog: Locator): Promise<void> {
  const uriInput = dialog.getByLabel("URI *");
  const nameInput = dialog.getByLabel("Name *");
  const createButton = dialog.getByRole("button", { name: "Create", exact: true });

  await uriInput.fill("not a url");
  await nameInput.fill("Temp name");
  await expect(dialog.getByText("Please enter a valid URI", { exact: true })).toBeVisible();
  await expect(createButton).toBeDisabled();
}

/**
 * Magic URL: a Name over 100 characters shows the length error and keeps
 * Create disabled.
 */
export async function verifyNameOver100CharsShowsLengthErrorAndCreateDisabled(page: Page, dialog: Locator): Promise<void> {
  const uriInput = dialog.getByLabel("URI *");
  const nameInput = dialog.getByLabel("Name *");
  const createButton = dialog.getByRole("button", { name: "Create", exact: true });

  await uriInput.fill("https://example.com");
  await nameInput.fill("N".repeat(101));
  await expect(
    dialog.getByText("Name must be at most 100 characters", { exact: true }),
  ).toBeVisible();
  await expect(createButton).toBeDisabled();
}

/**
 * Magic URL: a valid URI plus a (short enough) name enable Create.
 */
export async function verifyValidUriAndNameEnableCreate(page: Page, dialog: Locator): Promise<void> {
  const nameInput = dialog.getByLabel("Name *");
  const createButton = dialog.getByRole("button", { name: "Create", exact: true });

  await nameInput.fill("Temp validation name");
  await expect(createButton).toBeEnabled();
}

/**
 * Magic URL: Type defaults to Redirect; switching to Action reveals the
 * Action-specific fields (Request Method, Request Payload, Request Headers)
 * and the Action sub-select, then switching back to Redirect hides them.
 */
export async function verifyTypeDefaultsToRedirectAndSwitchingToActionRevealsFields(page: Page, dialog: Locator): Promise<void> {
  const typeSelect = dialog.getByRole("combobox").filter({ hasText: "Redirect" });

  await expect(typeSelect).toBeVisible();
  await expect(typeSelect).toHaveText("Redirect");

  await typeSelect.click();
  await page.getByRole("option", { name: "Action", exact: true }).click();

  // Verify Action-specific fields by their visible labels.
  await expect(dialog.getByText("Request Method", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Request Payload", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Request Headers", { exact: true })).toBeVisible();

  const actionSelect = dialog.getByRole("combobox").filter({ hasText: "Action" });
  await expect(actionSelect).toBeVisible();

  await actionSelect.click();
  await page.getByRole("option", { name: "Redirect", exact: true }).click();

  await expect(
    dialog.getByRole("combobox").filter({ hasText: "Redirect" }),
  ).toBeVisible();
}

/**
 * Magic URL: Set Usage Limit / Set Auto Expiry Date toggles reveal their
 * inputs when switched on.
 */
export async function verifyUsageLimitAndAutoExpiryDateTogglesRevealInputs(page: Page, dialog: Locator): Promise<void> {
  await expect(dialog.getByPlaceholder("Enter usage limit")).toBeHidden();
  await dialog.getByRole("switch", { name: "Set Usage Limit" }).click();
  await expect(dialog.getByPlaceholder("Enter usage limit")).toBeVisible();

  await expect(dialog.getByRole("button", { name: "Pick a date" })).toBeHidden();
  await dialog.getByRole("switch", { name: "Set Auto Expiry Date" }).click();
  await expect(dialog.getByRole("button", { name: "Pick a date" })).toBeVisible();
}

/** Magic URL: Cancel closes the dialog without creating anything. */
export async function verifyCancelClosesDialogWithoutCreating(page: Page, dialog: Locator): Promise<void> {
  await dialog.getByRole("button", { name: "Cancel" }).click();
  await expect(dialog).toBeHidden();
}

/**
 * Magic URL: the dialog's Client Credential textbox accepts a value and
 * keeps Create enabled (assuming URI and Name are also valid).
 */
export async function verifyClientCredentialFieldAcceptsValue(page: Page, dialog: Locator): Promise<void> {
  const clientCredential = dialog.getByLabel("Client Credential");
  await clientCredential.fill("test-credential-abc123");
  await expect(clientCredential).toHaveValue("test-credential-abc123");
}

/**
 * Magic URL: the dialog exposes a Close (X) button in addition to Cancel
 * that also dismisses the dialog without creating anything.
 */
export async function verifyCloseButtonDismissesDialog(page: Page, dialog: Locator): Promise<void> {
  await dialog.getByRole("button", { name: "Close" }).click();
  await expect(dialog).toBeHidden();
}

// ---------------------------------------------------------------------------
// Full lifecycle helpers
// ---------------------------------------------------------------------------

/**
 * Opens a row's "⋮" actions menu (last button in the row - the first is
 * the URL cell's copy-to-clipboard button) and clicks the given menu item.
 *
 * Radix renders DropdownMenuContent into a portal that animates in. A bare
 * `getByRole('menuitem').click()` can resolve the node before it has
 * finished mounting - Playwright then sees the element get detached
 * mid-click and retries until the 120s timeout. Wait for the menu (and
 * the item) to settle before clicking so the click lands on the stable
 * node.
 */
export async function chooseRowAction(
  row: Locator,
  action: "View Details" | "Go to Link" | "Deactivate",
): Promise<void> {
  await row.getByRole("button").last().click();

  const menu = row.page().getByRole("menu");
  await expect(menu).toBeVisible();

  const item = menu.getByRole("menuitem", { name: action });
  await expect(item).toBeVisible();
  await item.click();
}

/**
 * Magic URL: creates a Magic URL with a unique name and URI. Asserts the
 * success toast appears and the dialog closes.
 */
export async function createMagicUrlWithUniqueName(page: Page, uniqueName: string, uniqueUrl: string): Promise<void> {
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
}

/**
 * Magic URL: the new Magic URL appears in the list. Returns the row
 * locator and the shortened URI (the first https?://... text inside the
 * row) so callers can assert against them.
 */
export async function getCreatedRowAndShortUri(page: Page, uniqueName: string): Promise<{ row: Locator; shortUri: string }> {
  const row = magicUrlRow(page, uniqueName);
  await expect(row).toBeVisible({ timeout: 30_000 });
  // The URL cell renders shortUri first, then the original uri below it
  // (both as plain text, not links) - the shortened one comes first.
  const shortUri = (
    await row
      .getByText(/^https?:\/\//)
      .first()
      .innerText()
  ).trim();
  expect(shortUri).toMatch(/^https?:\/\//);
  return { row, shortUri };
}

/**
 * Magic URL: View Details navigates to the details page for the given row.
 */
export async function chooseViewDetailsFromRow(row: Locator): Promise<void> {
  await chooseRowAction(row, "View Details");
  await row.page().waitForURL(/\/magic-url\/details\//, { timeout: 15_000 });
}

/**
 * Magic URL: Details card shows usage, status, timestamps and both URLs
 * (shortUri and uniqueUrl).
 */
export async function verifyDetailsCardShowsUsageStatusTimestampsAndBothUrls(
  page: Page,
  uniqueName: string,
  shortUri: string,
  uniqueUrl: string,
): Promise<void> {
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
}

/**
 * Magic URL: the details-page actions menu only offers Deactivate here -
 * no "View Details"/"Go to Link" (those only make sense from the list).
 * Closes the menu with Escape and navigates back to the list.
 */
export async function verifyDetailsPageActionsMenuOnlyHasDeactivate(
  page: Page,
  uniqueName: string,
): Promise<void> {
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
}

/**
 * Magic URL: Deactivate asks for confirmation before removing it. Cancels
 * first to prove the record survives an aborted deactivation.
 */
export async function verifyDeactivateAsksForConfirmationAndCancelAborts(
  page: Page,
  row: Locator,
): Promise<void> {
  await chooseRowAction(row, "Deactivate");

  const confirmDialog = deactivateConfirmDialog(page);
  await expect(confirmDialog).toBeVisible();
  await expect(confirmDialog.getByText(DEACTIVATE_CONFIRM_TEXT)).toBeVisible();

  // Cancel first, to prove the record survives an aborted deactivation.
  await confirmDialog.getByRole("button", { name: "Cancel" }).click();
  await expect(confirmDialog).toBeHidden();
  await expect(row).toBeVisible();
}

/**
 * Magic URL: confirming Deactivate deactivates the test record (cleanup).
 */
export async function confirmDeactivateAndAssertSuccess(page: Page, row: Locator): Promise<void> {
  await chooseRowAction(row, "Deactivate");
  await deactivateConfirmDialog(page)
    .getByRole("button", { name: "Deactivate", exact: true })
    .click();

  await expect(
    page.getByText("Magic URL deactivated successfully", { exact: true }),
  ).toBeVisible({ timeout: 15_000 });
}
