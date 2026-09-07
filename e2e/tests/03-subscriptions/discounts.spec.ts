import { test } from "../../support/test-base";
import {
  openDiscountsPage,
  verifyPageHeaderAndCatalogueAndNewDiscountCta,
  verifyCreateADiscountIntroCardVisible,
  verifyEmptyCatalogueShowsNoDiscountsAuthoredYet,
  verifyNewDiscountOpensWizardAndCancelReturnsToList,
  verifyCodeFieldRejectsUppercaseLetters,
  createStandardPercentageDiscount,
  verifyEditLoadsPrefilledWizardAndCancel,
  verifyRetireArchivesDiscount,
  verifyBackToPlansLinkReturnsToPlans,
} from "../../page/subscripptions/discounts";

test.describe("flow: Subscriptions - Discounts", () => {
  test("Discounts - catalogue, wizard (offer-type radios), create, edit, retire", async ({ page }) => {
    test.setTimeout(180_000);

    const stamp = Date.now();
    const code = `e2e-discount-${stamp}`;
    const name = `E2E Discount ${stamp}`;

    await openDiscountsPage(page);

    await test.step("[Positive] page header, catalogue card and New discount CTA are visible", () =>
      verifyPageHeaderAndCatalogueAndNewDiscountCta(page));
    await test.step("[Positive] 'Create a discount' intro card explains the three offer types", () =>
      verifyCreateADiscountIntroCardVisible(page));
    await test.step("[Positive] empty catalogue shows the 'No discounts authored yet' notice", () =>
      verifyEmptyCatalogueShowsNoDiscountsAuthoredYet(page));
    await test.step("[Positive] New discount opens the CampaignBuilder wizard on Step 1", () =>
      verifyNewDiscountOpensWizardAndCancelReturnsToList(page));
    await test.step("[Negative] the Code field rejects uppercase letters and keeps Next disabled", () =>
      verifyCodeFieldRejectsUppercaseLetters(page));
    await test.step("[Positive] creating a Standard percentage discount adds it to the catalogue", () =>
      createStandardPercentageDiscount(page, code, name, "15"));
    await test.step("[Positive] Edit loads the wizard pre-filled with the discount's values", () =>
      verifyEditLoadsPrefilledWizardAndCancel(page, name, code));
    await test.step("[Positive] Retire moves the discount to Archived (row stays, buttons disappear)", () =>
      verifyRetireArchivesDiscount(page, name, code));
    await test.step("[Positive] 'Back to plans' returns to the plans list", () =>
      verifyBackToPlansLinkReturnsToPlans(page));
  });
});
