import { test } from "../../support/test-base";
import { openPaymentsSubPage } from "../../support/auth-helpers";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";
import {
  verifyPageHeaderAndSearchControlsVisible,
  verifyEmptyStateShowsNoSavedMethods,
  verifyCreatePaymentShortcutNavigates,
} from "../../page/payments/saved-cards";

test.describe("flow: Payments — Saved Cards", () => {
  test("Saved cards — header, empty state, and 'Create payment' shortcut", async ({
    page,
  }) => {
    test.setTimeout(60_000);
    await openUtilitiesDashboard(page);
    await openPaymentsSubPage(page, "Saved Cards");

    await test.step("Saved cards: page header and search/filter controls are visible", () =>
      verifyPageHeaderAndSearchControlsVisible(page));
    await test.step("Saved cards: empty environment shows 'No saved payment methods'", () =>
      verifyEmptyStateShowsNoSavedMethods(page));
    await test.step("Saved cards: 'Create payment' shortcut navigates to Create Payment", () =>
      verifyCreatePaymentShortcutNavigates(page));
  });
});
