import { test, expect } from "../../support/test-base";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";
import {
  PROVIDER_ADYEN,
  PROVIDER_STRIPE,
  emptyProvidersBody,
  providersBody,
  stubPaymentsProvidersEndpoint,
  stubOrganizationsEndpoint,
  openPaymentProvidersList,
  verifyListPageHeader,
  verifyEmptyStateShowsNoProviderRegistered,
  verifyLoadingSkeletonAppears,
  verifyErrorStateSurfacesTryAgain,
  verifyProviderTableRendersRows,
  verifyStatusFilterEnabledOnly,
  verifyStatusFilterDisabledOnly,
  verifySearchInputFiltersByMerchant,
  verifySearchInputFiltersByProviderName,
  verifyEmptyFilterResultShowsNoProvidersMatch,
  verifyRefreshButtonRefetches,
  verifyCreateProviderLinkOpens,
  verifyCreateProviderFormLoadsWithAdyenDefaults,
  verifyWebhookEndpointsCardRenders,
  verifyIdentityKeysCardRenders,
  verifyBeforeCreatingCardRenders,
  verifyAdditionalOrgsCheckboxListAppears,
  verifyManualCaptureSwitchToggles,
  verifyStoreIdInputAcceptsValue,
  verifyEmptyMerchantIdShowsError,
  verifyEmptyApiKeyShowsError,
  verifyEmptyFrontendResultUrlShowsError,
  verifyNonHttpsFrontendResultUrlShowsError,
  verifyInvalidCountryCodeShowsError,
  verifyMaxRefundAgeOver3650SurfacesError,
  verifyEmptyCheckoutApiBaseUrlShowsError,
  verifyEmptyStandardHmacShowsError,
  verifyInvalidStandardHmacShowsError,
  verifyInvalidTokenHmacShowsError,
  verifySwitchingToStripeChangesLabels,
  verifyStripeApiKeyWithoutPrefixShowsError,
  verifyStripeWebhookSecretWithoutPrefixShowsError,
  verifyCreateProviderButtonNeverSubmitted,
  verifyCancelReturnsToListFromCreate,
  verifyRowEditOpensUpdatePage,
  verifyUpdateFormPrefilled,
  verifyUpdatePageExposesEnabledSwitch,
  verifyConcurrencyProtectedCardRenders,
  verifyCancelFromUpdateReturnsToList,
  verifyRowRotateOpensRotatePage,
  verifyRotateFormLoadedWithEmptyFields,
  verifyWebhookOverlapCardRenders,
  verifyProtectedOperationCardRenders,
  verifyRotateAllEmptySurfacesError,
  verifyAdyenRotateNonHexHmacShowsError,
  verifyCancelFromAdyenRotateReturnsToList,
  verifyRotatingStripeProviderSwitchesLabels,
  verifyStripeRotationRejectsNonWhsecSecret,
  verifyCancelFromStripeRotateReturnsToList,
  type PaymentsProvidersHolder,
} from "../../page/payments/payment-providers";

test.describe("flow: Payments — Payment Providers", () => {
  test("Payment Providers — list (empty/error/seeded), create, update, rotate (Adyen/Stripe)", async ({
    page,
  }) => {
    test.setTimeout(240_000);

    // Route stubs — holder's responder can be swapped at any point.
    const holder: PaymentsProvidersHolder = {
      responder: () => ({
        status: 200,
        contentType: "application/json",
        body: emptyProvidersBody(),
      }),
    };
    await stubPaymentsProvidersEndpoint(page, holder);
    await stubOrganizationsEndpoint(page);

    await openUtilitiesDashboard(page);
    await openPaymentProvidersList(page);

    // =================================================================
    // Section A — List page (empty/error states, no data)
    // =================================================================
    await test.step("[Positive] page header explains credentials are never returned", () =>
      verifyListPageHeader(page));
    await test.step("[Positive] empty environment shows 'No payment provider registered' with explanatory subtitle and Create CTA", () =>
      verifyEmptyStateShowsNoProviderRegistered(page));
    await test.step("[Positive] loading skeleton appears while providers are being fetched", () =>
      verifyLoadingSkeletonAppears(page, holder));
    await test.step("[Negative] error state surfaces 'Providers could not be loaded' with Try again", () =>
      verifyErrorStateSurfacesTryAgain(page, holder));

    // =================================================================
    // Section B — List page (with mocked providers)
    // =================================================================
    holder.responder = () => ({
      status: 200,
      contentType: "application/json",
      body: providersBody([PROVIDER_ADYEN, PROVIDER_STRIPE]),
    });
    await page.reload();
    await expect(
      page.getByRole("heading", { name: "Registered providers" }),
    ).toBeVisible();

    await test.step("[Positive] provider table renders rows with provider, merchant, organization, country, capture, status, version and actions", () =>
      verifyProviderTableRendersRows(page));
    await test.step("[Positive] status filter narrows the table to only Enabled rows", () =>
      verifyStatusFilterEnabledOnly(page));
    await test.step("[Positive] status filter narrows to only Disabled rows", () =>
      verifyStatusFilterDisabledOnly(page));
    await test.step("[Positive] search input filters rows by merchant id", () =>
      verifySearchInputFiltersByMerchant(page));
    await test.step("[Positive] search input filters rows by provider name", () =>
      verifySearchInputFiltersByProviderName(page));
    await test.step("[Positive] empty filter result shows 'No providers match these filters' (no Create CTA)", () =>
      verifyEmptyFilterResultShowsNoProvidersMatch(page));
    await test.step("[Positive] Refresh button re-fetches the provider list", () =>
      verifyRefreshButtonRefetches(page));

    // =================================================================
    // Section C — Create page (from list)
    // =================================================================
    await test.step("[Positive] Create provider link opens the registration page", () =>
      verifyCreateProviderLinkOpens(page));
    await test.step("[Positive] Create Payment Provider form loads with Adyen defaults", () =>
      verifyCreateProviderFormLoadsWithAdyenDefaults(page));
    await test.step("[Positive] Webhook endpoints card renders with copy controls", () =>
      verifyWebhookEndpointsCardRenders(page));
    await test.step("[Positive] Identity keys card renders on the create page", () =>
      verifyIdentityKeysCardRenders(page));
    await test.step("[Positive] Before creating card renders on the create page", () =>
      verifyBeforeCreatingCardRenders(page));
    await test.step("[Positive] Additional organizations checkbox list appears once a primary org is picked", () =>
      verifyAdditionalOrgsCheckboxListAppears(page));
    await test.step("[Positive] Manual capture switch toggles state", () =>
      verifyManualCaptureSwitchToggles(page));
    await test.step("[Positive] Store ID input accepts a value", () =>
      verifyStoreIdInputAcceptsValue(page));

    await test.step("[Negative] empty Merchant ID shows required error", () =>
      verifyEmptyMerchantIdShowsError(page));
    await test.step("[Negative] empty API key shows required error", () =>
      verifyEmptyApiKeyShowsError(page));
    await test.step("[Negative] empty Frontend result URL shows the exact required error", () =>
      verifyEmptyFrontendResultUrlShowsError(page));
    await test.step("[Negative] non-HTTPS Frontend result URL shows the exact error", () =>
      verifyNonHttpsFrontendResultUrlShowsError(page));
    await test.step("[Negative] invalid Country code shows the exact error", () =>
      verifyInvalidCountryCodeShowsError(page));
    await test.step("[Negative] Maximum refund age over 3650 surfaces an error", () =>
      verifyMaxRefundAgeOver3650SurfacesError(page));
    await test.step("[Negative] empty Checkout API base URL (Adyen) shows the Adyen-specific required error", () =>
      verifyEmptyCheckoutApiBaseUrlShowsError(page));
    await test.step("[Negative] empty Standard webhook HMAC shows the required-length error", () =>
      verifyEmptyStandardHmacShowsError(page));
    await test.step("[Negative] invalid Standard webhook HMAC shows the exact hex error", () =>
      verifyInvalidStandardHmacShowsError(page));
    await test.step("[Negative] invalid Adyen Token webhook HMAC shows the exact hex error", () =>
      verifyInvalidTokenHmacShowsError(page));
    await test.step("[Positive] switching Provider to Stripe changes labels and hides Token webhook HMAC and Checkout API base URL", () =>
      verifySwitchingToStripeChangesLabels(page));
    await test.step("[Negative] a Stripe API key without sk_/rk_ shows the exact prefix error", () =>
      verifyStripeApiKeyWithoutPrefixShowsError(page));
    await test.step("[Negative] a Stripe webhook secret without whsec_ shows the exact prefix error", () =>
      verifyStripeWebhookSecretWithoutPrefixShowsError(page));
    await test.step("[Security] no real provider is ever registered by this test (Create provider is never submitted)", () =>
      verifyCreateProviderButtonNeverSubmitted(page));
    await test.step("[Positive] Cancel returns to the Payment Providers list without creating anything", () =>
      verifyCancelReturnsToListFromCreate(page));

    // =================================================================
    // Section D — Update page (from row Edit)
    // =================================================================
    await test.step("[Positive] row Edit action opens the Update page with provider identity section", () =>
      verifyRowEditOpensUpdatePage(page));
    await test.step("[Positive] Update form is pre-filled from the provider record", () =>
      verifyUpdateFormPrefilled(page));
    await test.step("[Positive] Update page exposes a Provider enabled switch (only on update)", () =>
      verifyUpdatePageExposesEnabledSwitch(page));
    await test.step("[Positive] Concurrency protected card renders on the update page", () =>
      verifyConcurrencyProtectedCardRenders(page));
    await test.step("[Positive] Cancel from update returns to the list", () =>
      verifyCancelFromUpdateReturnsToList(page));

    // =================================================================
    // Section E — Rotate page (Adyen provider, from row Rotate)
    // =================================================================
    await test.step("[Positive] row Rotate action opens the Rotate page with provider identity section", () =>
      verifyRowRotateOpensRotatePage(page));
    await test.step("[Positive] Rotate form is loaded with all credential fields empty", () =>
      verifyRotateFormLoadedWithEmptyFields(page));
    await test.step("[Positive] Webhook overlap card renders on the rotate page", () =>
      verifyWebhookOverlapCardRenders(page));
    await test.step("[Positive] Protected operation card renders on the rotate page", () =>
      verifyProtectedOperationCardRenders(page));
    await test.step("[Negative] submitting with all fields empty surfaces 'Enter at least one credential to rotate.'", () =>
      verifyRotateAllEmptySurfacesError(page));
    await test.step("[Negative] Adyen rotate non-hex HMAC shows the exact error", () =>
      verifyAdyenRotateNonHexHmacShowsError(page));
    await test.step("[Positive] Cancel from rotate returns to the list", () =>
      verifyCancelFromAdyenRotateReturnsToList(page));

    // =================================================================
    // Section F — Rotate page (Stripe provider)
    // =================================================================
    await test.step("[Positive] rotating the Stripe provider switches the labels to Stripe-specific copy", () =>
      verifyRotatingStripeProviderSwitchesLabels(page));
    await test.step("[Negative] Stripe rotation rejects a token-webhook HMAC with the exact error", () =>
      verifyStripeRotationRejectsNonWhsecSecret(page));
    await test.step("[Positive] Cancel from Stripe rotate returns to the list", () =>
      verifyCancelFromStripeRotateReturnsToList(page));
  });
});
