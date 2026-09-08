import { test } from "../../support/test-base";
import {
  openMerchantProfile,
  verifyPageHeaderAndIdentityAndBrandingControlsVisible,
  verifyInheritedVsOwnIdentityBannerVisible,
  editIdentityAndSave,
  verifyReloadPreservesSavedProfile,
  editBrandingColorsAndSave,
  verifyAddressAndTaxFieldsVisible,
  verifySubscriptionPaymentProviderSectionVisible,
  verifyLogoUploadAndConsoleOnlyFooterVisible,
  verifyBackToPlansLinkReturnsToPlans,
  // isSaveEnabled,
} from "../../page/subscripptions/merchant-profile";

test.describe("flow: Subscriptions - Merchant profile", () => {
  test("Merchant profile - header, address, identity, branding, provider selector, logo, save, reload", async ({
    page,
  }) => {
    test.setTimeout(240_000);

    const stamp = Date.now().toString();

    await openMerchantProfile(page);

    await test.step("[Positive] page header, identity fields, branding controls are visible", () =>
      verifyPageHeaderAndIdentityAndBrandingControlsVisible(page));
    await test.step("[Positive] address + tax/VAT registration fields are visible", () =>
      verifyAddressAndTaxFieldsVisible(page));
    await test.step("[Positive] subscription payment provider section exposes Stripe and Adyen", () =>
      verifySubscriptionPaymentProviderSectionVisible(page));
    await test.step("[Positive] invoice branding exposes logo upload and console-only footer notice", () =>
      verifyLogoUploadAndConsoleOnlyFooterVisible(page));
    await test.step("[Positive] inherited vs own identity is reported on the page", () =>
      verifyInheritedVsOwnIdentityBannerVisible(page));

    // Detect Save availability once and skip the save/round-trip steps
    // when the tenant has no payment provider configured (the Save button
    // is disabled with a helpful message in that case).
    // const saveEnabled = await isSaveEnabled(page);
    // test.skip(
    //   !saveEnabled,
    //   "Save merchant profile is disabled because no payment provider is configured for this tenant",
    // );

    await test.step("[Positive] editing identity, support email and saving updates the profile", () =>
      editIdentityAndSave(page, stamp));
    await test.step("[Positive] a reload re-fetches the saved profile with values preserved", () =>
      verifyReloadPreservesSavedProfile(page, stamp));
    await test.step("[Positive] invoice branding colors can be edited and saved", () =>
      editBrandingColorsAndSave(page));
    await test.step("[Positive] 'Back to plans' returns to the plans list without saving", () =>
      verifyBackToPlansLinkReturnsToPlans(page));
  });
});
