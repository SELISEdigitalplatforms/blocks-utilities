import { test } from "../../support/test-base";
import {
  openPlans,
  verifyPageHeaderAndOrganizationAndSearchVisible,
  verifyHeaderActionsIncludeDiscountsRefreshCreatePlan,
  verifyCatalogueTabsAndSortAndStatsVisible,
  verifyEmptyCatalogueShowsTenantWideNotice,
  verifySearchNarrowsCatalogue,
  verifyCreatePlanOpensWizardStep1,
  verifyWizardIdentityStepExtrasVisible,
  verifyBackIsDisabledOnStep1,
  verifyFillingIdentityAdvancesToPricingModel,
  verifyBackIsEnabledOnStep2,
  verifyBackReturnsToIdentityPreservingValues,
  verifyBlankDisplayNameIsRejectedAtReview,
  createFlatFeePlanThroughWizard,
  verifyLandsOnPlanDetailPage,
  verifyDetailPageActionsExposeDuplicateAndEdit,
  addSecondPriceThroughEditor,
  verifyDuplicatePlanPrefillsWizardWithBlankCode,
  verifyEditOpensBuilderWithIdentityLocked,
  editDescriptionAndSaveReturnsToDetailPage,
  verifyPlanIsDiscoverableFromListByCode,
  verifyDiscountsLinkNavigatesToDiscountsPage,
  verifyBackToPlansLinkReturnsToPlans,
} from "../../page/subscripptions/plans";

test.describe("flow: Subscriptions - Plans", () => {
  test("Plans - catalogue, 5-step wizard, add price, duplicate, edit, discovery, discounts link", async ({
    page,
  }) => {
    test.setTimeout(240_000);

    const uniqueSuffix = Date.now();
    const displayName = `E2E Flat Plan ${uniqueSuffix}`;
    const code = `e2e-flat-${uniqueSuffix}`;

    await openPlans(page);

    await test.step("[Positive] page header and organization/search controls are visible", () =>
      verifyPageHeaderAndOrganizationAndSearchVisible(page));
    await test.step("[Positive] header actions include Discounts, Refresh plans and Create plan", () =>
      verifyHeaderActionsIncludeDiscountsRefreshCreatePlan(page));
    await test.step("[Positive] catalogue renders Active/Archived/All tabs, Sort plans combobox and stats strip", () =>
      verifyCatalogueTabsAndSortAndStatsVisible(page));
    await test.step("[Positive] empty catalogue shows the tenant-wide plans notice", () =>
      verifyEmptyCatalogueShowsTenantWideNotice(page));
    await test.step("[Positive] search narrows the plan catalogue", () =>
      verifySearchNarrowsCatalogue(page));
    await test.step("[Positive] Create plan opens the 5-step wizard on step 1 (Identity) with all step labels visible", () =>
      verifyCreatePlanOpensWizardStep1(page));
    await test.step("[Positive] Identity step exposes Organization, Description, Family code, Family rank, Advanced JSON and Plan preview", () =>
      verifyWizardIdentityStepExtrasVisible(page));
    await test.step("[Positive] Back is disabled on step 1", () =>
      verifyBackIsDisabledOnStep1(page));
    await test.step("[Positive] filling Identity and clicking Next advances to Pricing model and Back is enabled", () =>
      verifyFillingIdentityAdvancesToPricingModel(
        page,
        `E2E Wizard Plan ${uniqueSuffix}`,
        `e2e-wizard-${uniqueSuffix}`,
      ));
    await test.step("[Positive] Back is enabled on step 2 (Pricing model)", () =>
      verifyBackIsEnabledOnStep2(page));
    await test.step("[Positive] Back returns to Identity with the entered values preserved", () =>
      verifyBackReturnsToIdentityPreservingValues(page));
    await test.step("[Negative] clicking Next with a blank Display name is rejected at Identity", () =>
      verifyBlankDisplayNameIsRejectedAtReview(page));
    await test.step("[Positive] create a minimal flat-fee plan through the wizard", () =>
      createFlatFeePlanThroughWizard(page, displayName, code, "19.00"));
    await test.step("[Positive] lands on the plan detail page with the new plan's data", () =>
      verifyLandsOnPlanDetailPage(page, displayName));
    await test.step("[Positive] detail page exposes Duplicate plan and Edit, and no separate add-price entry", () =>
      verifyDetailPageActionsExposeDuplicateAndEdit(page));
    await test.step("[Positive] a second price is added through the plan editor", () =>
      addSecondPriceThroughEditor(page, displayName, "29.00"));
    await test.step("[Positive] Duplicate plan pre-fills the wizard from this plan with a blank code", () =>
      verifyDuplicatePlanPrefillsWizardWithBlankCode(page, displayName));
    await test.step("[Positive] Edit opens the builder with identity fields locked", () =>
      verifyEditOpensBuilderWithIdentityLocked(page, displayName));
    await test.step("[Positive] editing the description and saving returns to the detail page", () =>
      editDescriptionAndSaveReturnsToDetailPage(
        page,
        displayName,
        `Updated by e2e at ${uniqueSuffix}`,
      ));
    await test.step("[Positive] the plan is discoverable from the plan list by its code", () =>
      verifyPlanIsDiscoverableFromListByCode(page, displayName, code));
    await test.step("[Positive] Discounts navigates to the discounts page", () =>
      verifyDiscountsLinkNavigatesToDiscountsPage(page));
    await test.step("[Positive] 'Back to plans' returns to the plans list without saving", () =>
      verifyBackToPlansLinkReturnsToPlans(page));
  });
});
