import { test } from "../../support/test-base";
import {
  openBillingProfile,
  verifyPageHeaderAndRequiredFieldsVisible,
  verifyIncompleteNoticeSurfacesOrCompleteBadge,
  fillIdentityAndSave,
  verifyReloadPreservesIdentityValues,
  editOptionalFieldsAndSave,
  verifyInvalidCountryCodeIsRejected,
  verifyAddressSectionFieldsAreVisible,
  verifyTaxOrVatRegistrationFieldVisible,
  verifyFooterNoticeAboutIssuedInvoices,
  verifyBackToPlansLinkReturnsToPlans,
} from "../../page/subscripptions/billing-profile";

test.describe("flow: Subscriptions - Billing profile", () => {
  test("Billing profile - header, address, identity, save, reload, optional fields, invalid country code, footer", async ({ page }) => {
    test.setTimeout(180_000);

    const stamp = Date.now().toString();

    await openBillingProfile(page);

    await test.step("[Positive] page header and required fields are visible", () =>
      verifyPageHeaderAndRequiredFieldsVisible(page));
    await test.step("[Positive] address section exposes Address (line 1+2), Postal code, City, State or region", () =>
      verifyAddressSectionFieldsAreVisible(page));
    await test.step("[Positive] Tax or VAT registration field is visible with its print-as-entered note", () =>
      verifyTaxOrVatRegistrationFieldVisible(page));
    await test.step("[Positive] footer notice explains editing never changes an already-issued invoice", () =>
      verifyFooterNoticeAboutIssuedInvoices(page));
    await test.step("[Positive] an incomplete profile surfaces the 'not complete yet' notice", () =>
      verifyIncompleteNoticeSurfacesOrCompleteBadge(page));
    await test.step("[Positive] filling identity and saving turns the profile complete", () =>
      fillIdentityAndSave(page, stamp));
    await test.step("[Positive] the saved profile reloads with the entered values preserved", () =>
      verifyReloadPreservesIdentityValues(page, stamp));
    await test.step("[Positive] editing optional fields and saving updates the profile", () =>
      editOptionalFieldsAndSave(page, stamp));
    await test.step("[Negative] an invalid country code is rejected by the server", () =>
      verifyInvalidCountryCodeIsRejected(page));
    await test.step("[Positive] 'Back to plans' returns to the plans list without saving", () =>
      verifyBackToPlansLinkReturnsToPlans(page));
  });
});
