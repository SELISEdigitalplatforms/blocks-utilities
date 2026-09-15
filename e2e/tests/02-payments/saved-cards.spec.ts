import { test } from "../../support/test-base";
import { openPaymentsSubPage } from "../../support/auth-helpers";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";
import {
  verifyPageHeaderAndSearchControlsVisible,
  verifyEmptyStateShowsNoSavedMethods,
  verifyCreatePaymentShortcutNavigates,
  emptySavedCardsResponder,
  stubSavedCardsEndpoint,
  verifySeededCardsRenderRows,
  verifySearchNarrowsSeededCards,
  verifyRemoveCardShowsRemovedToast,
  type SavedCardsHolder,
} from "../../page/payments/saved-cards";

test.describe("flow: Payments — Saved Cards", () => {
  test("Saved cards — header, empty state, and 'Create payment' shortcut", async ({
    page,
  }) => {
    test.setTimeout(120_000);
    const holder: SavedCardsHolder = { responder: emptySavedCardsResponder() };
    await stubSavedCardsEndpoint(page, holder);
    await openUtilitiesDashboard(page);
    await openPaymentsSubPage(page, "Saved Cards");

    await test.step("Saved cards: page header and search/filter controls are visible", () =>
      verifyPageHeaderAndSearchControlsVisible(page));
    await test.step("Saved cards: empty environment shows 'No saved payment methods'", () =>
      verifyEmptyStateShowsNoSavedMethods(page));
    await test.step("Saved cards: seeded list renders both stubbed cards", () =>
      verifySeededCardsRenderRows(page, holder));
    await test.step("Saved cards: search narrows to the matching brand", () =>
      verifySearchNarrowsSeededCards(page));
    await test.step("Saved cards: stubbed remove shows the removed toast", () =>
      verifyRemoveCardShowsRemovedToast(page));
    await test.step("Saved cards: 'Create payment' shortcut navigates to Create Payment", () =>
      verifyCreatePaymentShortcutNavigates(page));
  });
});
