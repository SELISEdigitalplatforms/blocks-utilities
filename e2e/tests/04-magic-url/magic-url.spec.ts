import { test, expect } from "../../support/test-base";
import {
  openMagicUrl,
  verifyTableShowsAllExpectedColumns,
  verifyFilterControlsAvailable,
  verifySearchInputAvailable,
  verifySortableColumnHeaders,
  verifyEmptyEnvironmentShowsNoResults,
  openCreateMagicUrlDialog,
  verifyCreateStaysDisabledWithEmptyForm,
  verifyInvalidUriShowsErrorAndCreateDisabled,
  verifyNameOver100CharsShowsLengthErrorAndCreateDisabled,
  verifyValidUriAndNameEnableCreate,
  verifyClientCredentialFieldAcceptsValue,
  verifyTypeDefaultsToRedirectAndSwitchingToActionRevealsFields,
  verifyUsageLimitAndAutoExpiryDateTogglesRevealInputs,
  verifyCloseButtonDismissesDialog,
  verifyCancelClosesDialogWithoutCreating,
  createMagicUrlWithUniqueName,
  getCreatedRowAndShortUri,
  chooseViewDetailsFromRow,
  verifyDetailsCardShowsUsageStatusTimestampsAndBothUrls,
  verifyDetailsPageActionsMenuOnlyHasDeactivate,
  verifyDeactivateAsksForConfirmationAndCancelAborts,
  confirmDeactivateAndAssertSuccess,
  magicUrlRow,
} from "../../page/magic-url/magic-url";

test.describe("flow: Magic URL", () => {
  test("Magic URL - list, create-dialog validation, full lifecycle (create/view/deactivate)", async ({
    page,
  }) => {
    test.setTimeout(240_000);

    await openMagicUrl(page);

    // =================================================================
    // Section A - List page
    // =================================================================
    await test.step("[Positive] table shows all expected columns", () =>
      verifyTableShowsAllExpectedColumns(page));
    await test.step("[Positive] URL and Name column headers are rendered as sortable buttons", () =>
      verifySortableColumnHeaders(page));
    await test.step("[Positive] filter controls are available", () =>
      verifyFilterControlsAvailable(page));
    await test.step("[Positive] a Search input is available above the table", () =>
      verifySearchInputAvailable(page));
    await test.step("[Positive] empty environment shows 'No results.'", () =>
      verifyEmptyEnvironmentShowsNoResults(page));

    // =================================================================
    // Section B - Create-dialog validation
    // =================================================================
    const dialog = await openCreateMagicUrlDialog(page);

    await test.step("[Negative] Create stays disabled while the form is empty", () =>
      verifyCreateStaysDisabledWithEmptyForm(page, dialog));
    await test.step("[Negative] invalid URI shows 'Please enter a valid URI'", () =>
      verifyInvalidUriShowsErrorAndCreateDisabled(page, dialog));
    await test.step("[Negative] Name over 100 characters shows the length error", () =>
      verifyNameOver100CharsShowsLengthErrorAndCreateDisabled(page, dialog));
    await test.step("[Positive] a valid URI and name enable Create", () =>
      verifyValidUriAndNameEnableCreate(page, dialog));
    await test.step("[Positive] Client Credential field accepts a value", () =>
      verifyClientCredentialFieldAcceptsValue(page, dialog));
    await test.step("[Positive] Type defaults to Redirect; switching to Action reveals extra fields", () =>
      verifyTypeDefaultsToRedirectAndSwitchingToActionRevealsFields(page, dialog));
    await test.step("[Positive] Set Usage Limit / Set Auto Expiry Date toggles reveal their inputs", () =>
      verifyUsageLimitAndAutoExpiryDateTogglesRevealInputs(page, dialog));
    await test.step("[Positive] Close (X) button dismisses the dialog without creating anything", () =>
      verifyCloseButtonDismissesDialog(page, dialog));

    // Re-open the dialog so the Cancel step exercises the same instance the
    // user has been working with.
    const dialogForCancel = await openCreateMagicUrlDialog(page);
    await test.step("[Positive] Cancel closes the dialog without creating anything", () =>
      verifyCancelClosesDialogWithoutCreating(page, dialogForCancel));

    // =================================================================
    // Section C - Full lifecycle (create -> view -> deactivate)
    // =================================================================
    const uniqueName = `e2e-magic-url-${Date.now()}`;
    const uniqueUrl = `https://example.com/e2e-${Date.now()}`;

    await test.step("[Positive] create a Magic URL with a unique name and URI", () =>
      createMagicUrlWithUniqueName(page, uniqueName, uniqueUrl));

    const { row, shortUri } = await getCreatedRowAndShortUri(page, uniqueName);
    expect(row).toBeTruthy();
    expect(shortUri).toMatch(/^https?:\/\//);

    await test.step("[Positive] View Details navigates to the details page", () =>
      chooseViewDetailsFromRow(row));
    await test.step("[Positive] Details card shows usage, status, timestamps and both URLs", () =>
      verifyDetailsCardShowsUsageStatusTimestampsAndBothUrls(page, uniqueName, shortUri, uniqueUrl));
    await test.step("[Security] the details-page actions menu only offers Deactivate here", () =>
      verifyDetailsPageActionsMenuOnlyHasDeactivate(page, uniqueName));

    // Re-fetch the row locator after navigating back to the list.
    const listRow = magicUrlRow(page, uniqueName);
    await expect(listRow).toBeVisible();

    await test.step("[Negative] Deactivate asks for confirmation before removing it", () =>
      verifyDeactivateAsksForConfirmationAndCancelAborts(page, listRow));
    await test.step("[Positive] confirming Deactivate deactivates the test record (cleanup)", () =>
      confirmDeactivateAndAssertSuccess(page, listRow));
  });
});
