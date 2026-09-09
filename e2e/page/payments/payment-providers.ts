import { expect, type Page, type Route } from "@playwright/test";
import { openPaymentsSubPage } from "../../support/auth-helpers";

/**
 * Payment Providers flows. Each function is a reusable step that exercises a
 * single provider-management behavior end-to-end (the click, the fill, the assertion).
 *
 * The /api/payments/providers endpoint is stubbed via a mutable responder
 * holder so flows can swap response bodies (empty / slow / error / seeded)
 * without re-installing the route. The spec creates the holder and passes
 * it to flows that need to mutate the responder.
 *
 * Assumes the caller has already opened the project dashboard via
 * openUtilitiesDashboard(page) so the sidebar is visible.
 */

export type PaymentsProvidersResponse = {
  status: number;
  contentType: string;
  body: string;
};

/** A responder may be sync or async (e.g. a never-resolving loading skeleton). */
export type PaymentsProvidersResponder = () =>
  | Promise<PaymentsProvidersResponse>
  | PaymentsProvidersResponse;

export type PaymentsProvidersHolder = {
  responder: PaymentsProvidersResponder;
};

/** Seeded Adyen provider fixture used by list / table / rotate steps. */
export const PROVIDER_ADYEN = {
  paymentProviderId: "pp-adyen-1",
  version: 1,
  providerName: "ADYEN-ONLINE",
  merchantId: "YourAdyenMerchant",
  organizationId: "org-acme",
  apiBaseUrl: "https://checkout-test.adyen.com/v72",
  returnUrl: null,
  frontendResultUrl: "https://app.example.com/app/foo/payment/result",
  countryCode: "CH",
  manualCapture: true,
  maxRefundDays: 365,
  storeId: null,
  isEnabled: true,
};

/** Seeded Stripe provider fixture used by list / table / rotate steps. */
export const PROVIDER_STRIPE = {
  paymentProviderId: "pp-stripe-1",
  version: 3,
  providerName: "STRIPE",
  merchantId: "acct_stripeMerchant",
  organizationId: "org-globex",
  apiBaseUrl: "",
  returnUrl: null,
  frontendResultUrl: "https://app.example.com/app/foo/payment/result",
  countryCode: "US",
  manualCapture: false,
  maxRefundDays: 180,
  storeId: "store_abc",
  isEnabled: false,
};

/** Empty providers response body. */
export function emptyProvidersBody(): string {
  return JSON.stringify({ success: true, data: [], error: null });
}

/** Wraps a list of items in the providers response envelope. */
export function providersBody(items: unknown[]): string {
  return JSON.stringify({ success: true, data: items, error: null });
}

/** Error response body in the same envelope. */
export function errorBody(message: string): string {
  return JSON.stringify({
    success: false,
    data: null,
    error: { code: "fetch_failed", message },
  });
}

/**
 * Install a mutable responder for /api/payments/providers. Non-GET requests
 * pass through so a stray click never silently creates a real provider.
 */
export async function stubPaymentsProvidersEndpoint(
  page: Page,
  holder: PaymentsProvidersHolder,
): Promise<void> {
  await page.route("**/api/payments/providers**", async (route: Route) => {
    const req = route.request();
    if (req.method() !== "GET") return route.continue();
    const response = await holder.responder();
    await route.fulfill(response);
  });
}

/** Install a stub for the organizations endpoint used by the form filters. */
export async function stubOrganizationsEndpoint(page: Page): Promise<void> {
  await page.route("**/organizations**", async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        organizations: [
          { itemId: "org-acme", name: "Acme Corp" },
          { itemId: "org-globex", name: "Globex Inc" },
        ],
        totalCount: 2,
      }),
    });
  });
}

/** Navigate to the Payment Providers list page and assert the header is visible. */
export async function openPaymentProvidersList(page: Page): Promise<void> {
  await openPaymentsSubPage(page, "Payment Providers");
  await expect(
    page.getByRole("heading", { name: "Payment providers" }),
  ).toBeVisible();
}

/** List page header explains that credentials are never returned. */
export async function verifyListPageHeader(page: Page): Promise<void> {
  await expect(
    page.getByRole("heading", { name: "Payment providers" }),
  ).toBeVisible();
  await expect(
    page.getByText(
      "Register and manage tenant-scoped provider configuration and credentials.",
    ),
  ).toBeVisible();
  await expect(
    page.getByText("Credentials are never returned by this endpoint."),
  ).toBeVisible();
}

/**
 * List page: empty environment shows 'No payment provider registered' with
 * explanatory subtitle and the inline Create provider CTA.
 */
export async function verifyEmptyStateShowsNoProviderRegistered(page: Page): Promise<void> {
  await expect(
    page.getByRole("heading", { name: "No payment provider registered" }),
  ).toBeVisible();
  await expect(
    page.getByText("Register a provider before creating payment sessions."),
  ).toBeVisible();
  // The empty state surfaces an inline Create provider button — distinct
  // from the header's link, so both should exist at once.
  await expect(
    page.getByRole("link", { name: "Create provider", exact: true }).first(),
  ).toBeVisible();
}

/**
 * List page: loading skeleton appears while providers are being fetched.
 * Swaps the responder to a slow one, reloads, observes the skeleton, then
 * restores the default empty responder and reloads again.
 */
export async function verifyLoadingSkeletonAppears(
  page: Page,
  holder: PaymentsProvidersHolder,
): Promise<void> {
  holder.responder = () =>
    new Promise<PaymentsProvidersResponse>(() => {
      // never resolves for the duration of this step
    });
  await page.reload();
  await expect(page.locator('[aria-label="Loading providers"]')).toBeVisible();

  holder.responder = () => ({
    status: 200,
    contentType: "application/json",
    body: emptyProvidersBody(),
  });
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "No payment provider registered" }),
  ).toBeVisible();
}

/**
 * List page: error state surfaces 'Providers could not be loaded' with
 * a Try again button. Swaps the responder to an error body, reloads,
 * verifies the error UI, then restores and clicks Try again to recover.
 */
export async function verifyErrorStateSurfacesTryAgain(
  page: Page,
  holder: PaymentsProvidersHolder,
): Promise<void> {
  holder.responder = () => ({
    status: 200,
    contentType: "application/json",
    body: errorBody("Upstream timeout"),
  });
  await page.reload();
  await expect(
    page.getByText("Providers could not be loaded", { exact: true }),
  ).toBeVisible();
  await expect(page.getByRole("button", { name: "Try again" })).toBeVisible();

  holder.responder = () => ({
    status: 200,
    contentType: "application/json",
    body: emptyProvidersBody(),
  });
  await page.getByRole("button", { name: "Try again" }).click();
  await expect(
    page.getByRole("heading", { name: "No payment provider registered" }),
  ).toBeVisible();
}

/**
 * List page (seeded): provider table renders rows with provider, merchant,
 * organization, country, capture, status, version and actions.
 */
export async function verifyProviderTableRendersRows(page: Page): Promise<void> {
  const table = page.getByRole("table");
  await expect(table.getByText("Adyen Hosted Checkout")).toBeVisible();
  await expect(table.getByText("YourAdyenMerchant")).toBeVisible();
  await expect(table.getByText("Acme Corp")).toBeVisible();
  await expect(table.getByText("CH", { exact: true })).toBeVisible();
  await expect(table.getByText("Manual", { exact: true })).toBeVisible();
  await expect(table.getByText("Enabled", { exact: true })).toBeVisible();
  await expect(table.getByText("1", { exact: true })).toBeVisible();

  await expect(table.getByText("Stripe Checkout")).toBeVisible();
  await expect(table.getByText("acct_stripeMerchant")).toBeVisible();
  await expect(table.getByText("Globex Inc")).toBeVisible();
  await expect(table.getByText("US", { exact: true })).toBeVisible();
  await expect(table.getByText("Automatic", { exact: true })).toBeVisible();
  await expect(table.getByText("Disabled", { exact: true })).toBeVisible();
  await expect(table.getByText("3", { exact: true })).toBeVisible();

  // Each row exposes Edit and Rotate actions.
  await expect(table.getByRole("link", { name: "Edit" }).first()).toBeVisible();
  await expect(table.getByRole("link", { name: "Rotate" }).first()).toBeVisible();
}

/** Status filter narrows the table to only Enabled rows. */
export async function verifyStatusFilterEnabledOnly(page: Page): Promise<void> {
  const statusSelect = page.locator('[role="combobox"]').filter({ hasText: "All statuses" });
  await statusSelect.click();
  await page.getByRole("option", { name: "Enabled", exact: true }).click();

  const table = page.getByRole("table");
  await expect(table.getByText("Adyen Hosted Checkout")).toBeVisible();
  await expect(table.getByText("Stripe Checkout")).toHaveCount(0);

  // Reset back to "all" for the next step.
  const resetSelect = page.locator('[role="combobox"]').filter({ hasText: "Enabled" });
  await resetSelect.click();
  await page.getByRole("option", { name: "All statuses", exact: true }).click();
  await expect(table.getByText("Stripe Checkout")).toBeVisible();
}

/** Status filter narrows to only Disabled rows. */
export async function verifyStatusFilterDisabledOnly(page: Page): Promise<void> {
  const statusSelect = page.locator('[role="combobox"]').filter({ hasText: "All statuses" });
  await statusSelect.click();
  await page.getByRole("option", { name: "Disabled", exact: true }).click();

  const table = page.getByRole("table");
  await expect(table.getByText("Stripe Checkout")).toBeVisible();
  await expect(table.getByText("Adyen Hosted Checkout")).toHaveCount(0);

  const resetSelect = page.locator('[role="combobox"]').filter({ hasText: "Disabled" });
  await resetSelect.click();
  await page.getByRole("option", { name: "All statuses", exact: true }).click();
}

/** Search input filters rows by merchant id. */
export async function verifySearchInputFiltersByMerchant(page: Page): Promise<void> {
  const search = page.getByRole("textbox", { name: "Search payment providers" });
  await search.click();
  await search.pressSequentially("stripeMerchant", { delay: 20 });

  const table = page.getByRole("table");
  await expect(table.getByText("Stripe Checkout")).toBeVisible();
  await expect(table.getByText("Adyen Hosted Checkout")).toHaveCount(0);

  await search.fill("");
  await expect(table.getByText("Adyen Hosted Checkout")).toBeVisible();
  await expect(table.getByText("Stripe Checkout")).toBeVisible();
}

/** Search input filters rows by provider name. */
export async function verifySearchInputFiltersByProviderName(page: Page): Promise<void> {
  const search = page.getByRole("textbox", { name: "Search payment providers" });
  await search.fill("ADYEN");

  const table = page.getByRole("table");
  await expect(table.getByText("Adyen Hosted Checkout")).toBeVisible();
  await expect(table.getByText("Stripe Checkout")).toHaveCount(0);

  await search.fill("");
}

/**
 * Empty filter result shows 'No providers match these filters' (without
 * the Create CTA, which only appears on the truly-empty state).
 */
export async function verifyEmptyFilterResultShowsNoProvidersMatch(page: Page): Promise<void> {
  const search = page.getByRole("textbox", { name: "Search payment providers" });
  await search.fill("nonexistent-merchant");

  await expect(
    page.getByRole("heading", { name: "No providers match these filters" }),
  ).toBeVisible();
  await expect(page.getByText("Change the search or status filter.")).toBeVisible();
  // The Create CTA only appears on the truly-empty state, not the
  // filter-empty state.
  await expect(
    page.getByRole("heading", { name: "No payment provider registered" }),
  ).toHaveCount(0);

  await search.fill("");
  // Back to the table.
  await expect(page.getByRole("table")).toBeVisible();
}

/** Refresh button re-fetches the provider list. */
export async function verifyRefreshButtonRefetches(page: Page): Promise<void> {
  const request = page.waitForRequest(
    (req) => req.url().includes("/api/payments/providers") && req.method() === "GET",
  );
  await page.getByRole("button", { name: "Refresh" }).click();
  await request;
  // Table is still rendered after refresh.
  await expect(page.getByRole("table")).toBeVisible();
}

/** Create provider link opens the registration page. */
export async function verifyCreateProviderLinkOpens(page: Page): Promise<void> {
  await page.getByRole("link", { name: "Create provider", exact: true }).first().click();
  await expect(
    page.getByRole("heading", { name: "Create payment provider" }),
  ).toBeVisible();
}

/** Create Payment Provider form loads with Adyen defaults. */
export async function verifyCreateProviderFormLoadsWithAdyenDefaults(page: Page): Promise<void> {
  await expect(page.getByRole("combobox", { name: "Provider" })).toHaveText(
    "Adyen Hosted Checkout",
  );
  await expect(
    page.getByRole("textbox", { name: "Checkout API base URL" }),
  ).toHaveValue("https://checkout-test.adyen.com/v72");
  await expect(
    page.getByRole("textbox", { name: "Frontend result URL" }),
  ).toHaveValue(/\/payment\/result$/);
  await expect(
    page.getByRole("spinbutton", { name: "Maximum refund age" }),
  ).toHaveValue("365");
  await expect(
    page.getByRole("combobox", { name: "Organization" }),
  ).toHaveText("Every organization in this tenant");
}

/** Webhook endpoints card renders with copy controls. */
export async function verifyWebhookEndpointsCardRenders(page: Page): Promise<void> {
  await expect(
    page.getByRole("heading", { name: "Webhook endpoints" }),
  ).toBeVisible();
  // Two endpoints are rendered for Adyen: Standard notifications and Token notifications.
  await expect(page.getByText("Standard notifications", { exact: true })).toBeVisible();
  await expect(page.getByText("Token notifications", { exact: true })).toBeVisible();
  // Two copy controls (one per endpoint).
  await expect(page.getByText(/^Copy /)).toHaveCount(2);
}

/** Identity keys card renders on the create page. */
export async function verifyIdentityKeysCardRenders(page: Page): Promise<void> {
  await expect(
    page.getByRole("heading", { name: "Identity keys" }),
  ).toBeVisible();
  await expect(page.getByText(/Return-state and shopper-reference keys/)).toBeVisible();
}

/** Before creating card renders on the create page. */
export async function verifyBeforeCreatingCardRenders(page: Page): Promise<void> {
  await expect(
    page.getByRole("heading", { name: "Before creating" }),
  ).toBeVisible();
  await expect(page.getByText(/Register the endpoints above in your provider/)).toBeVisible();
}

/**
 * Additional organizations checkbox list appears once a primary org is
 * picked; the primary org must NOT appear again.
 */
export async function verifyAdditionalOrgsCheckboxListAppears(page: Page): Promise<void> {
  await page.getByRole("combobox", { name: "Organization" }).click();
  await page.getByRole("option", { name: "Acme Corp", exact: true }).click();

  await expect(
    page.getByText("Also configure these organizations", { exact: true }),
  ).toBeVisible();
  // The primary org must NOT appear again.
  await expect(
    page.getByRole("checkbox", { name: "Acme Corp", exact: true }),
  ).toHaveCount(0);
  await expect(
    page.getByRole("checkbox", { name: "Globex Inc", exact: true }),
  ).toBeVisible();

  // Switch back to tenant-scope so subsequent fields stay on the default.
  await page.locator('[role="combobox"]').filter({ hasText: "Acme Corp" }).click();
  await page
    .getByRole("option", { name: "Every organization in this tenant", exact: true })
    .click();
}

/** Manual capture switch toggles state. */
export async function verifyManualCaptureSwitchToggles(page: Page): Promise<void> {
  const manualCaptureSwitch = page.getByRole("switch", { name: "Enable manual capture" });
  await expect(manualCaptureSwitch).not.toBeChecked();
  await manualCaptureSwitch.click();
  await expect(manualCaptureSwitch).toBeChecked();
  await manualCaptureSwitch.click();
  await expect(manualCaptureSwitch).not.toBeChecked();
}

/** Store ID input accepts a value. */
export async function verifyStoreIdInputAcceptsValue(page: Page): Promise<void> {
  const storeIdInput = page.getByRole("textbox", { name: "Store ID" });
  await storeIdInput.fill("store_abc");
  await expect(storeIdInput).toHaveValue("store_abc");
  await storeIdInput.fill("");
}

/** Empty Merchant ID shows required error. */
export async function verifyEmptyMerchantIdShowsError(page: Page): Promise<void> {
  const merchantIdInput = page.getByRole("textbox", { name: "Merchant ID" });
  await merchantIdInput.fill("");
  await merchantIdInput.blur();
  await expect(
    page
      .getByText("String must contain at least 1 character(s)", { exact: true })
      .first(),
  ).toBeVisible();
  await merchantIdInput.fill("YourAdyenMerchant");
}

/** Empty API key shows required error. */
export async function verifyEmptyApiKeyShowsError(page: Page): Promise<void> {
  const apiKeyInput = page.getByLabel("API key");
  await apiKeyInput.fill("");
  await apiKeyInput.blur();
  await expect(
    page
      .getByText("String must contain at least 1 character(s)", { exact: true })
      .first(),
  ).toBeVisible();
  await apiKeyInput.fill("test-api-key");
}

/** Empty Frontend result URL shows the exact required error. */
export async function verifyEmptyFrontendResultUrlShowsError(page: Page): Promise<void> {
  const frontendResultInput = page.getByRole("textbox", { name: "Frontend result URL" });
  await frontendResultInput.fill("");
  await frontendResultInput.blur();
  await expect(
    page.getByText("Enter the frontend result URL.", { exact: true }),
  ).toBeVisible();
  await frontendResultInput.fill("https://app.example.com/app/foo/payment/result");
}

/** Non-HTTPS Frontend result URL shows the exact error. */
export async function verifyNonHttpsFrontendResultUrlShowsError(page: Page): Promise<void> {
  const frontendResultInput = page.getByRole("textbox", { name: "Frontend result URL" });
  await frontendResultInput.fill("http://insecure.example.com/payment/result");
  await frontendResultInput.blur();
  await expect(page.getByText("Enter an absolute HTTPS URL.", { exact: true })).toBeVisible();
  await frontendResultInput.fill("https://app.example.com/app/foo/payment/result");
}

/** Invalid Country code shows the exact error. */
export async function verifyInvalidCountryCodeShowsError(page: Page): Promise<void> {
  // The input is capped at 2 characters, so use a 2-char non-letter value
  // to trip the regex refine.
  const countryInput = page.getByRole("textbox", { name: "Country code" });
  await countryInput.fill("U1");
  await countryInput.blur();
  await expect(
    page.getByText("Use a two-letter ISO country code.", { exact: true }),
  ).toBeVisible();
  await countryInput.fill("CH");
}

/** Maximum refund age over 3650 surfaces an error. */
export async function verifyMaxRefundAgeOver3650SurfacesError(page: Page): Promise<void> {
  const maxRefundInput = page.getByRole("spinbutton", { name: "Maximum refund age" });
  await maxRefundInput.fill("4000");
  await maxRefundInput.blur();
  await expect(
    page.getByText(/Number must be less than or equal to 3650/i).first(),
  ).toBeVisible();
  await maxRefundInput.fill("365");
}

/** Empty Checkout API base URL (Adyen) shows the Adyen-specific required error. */
export async function verifyEmptyCheckoutApiBaseUrlShowsError(page: Page): Promise<void> {
  const apiBaseUrlInput = page.getByRole("textbox", { name: "Checkout API base URL" });
  await apiBaseUrlInput.fill("");
  await apiBaseUrlInput.blur();
  await expect(
    page.getByText("Adyen requires its Checkout API base URL.", { exact: true }),
  ).toBeVisible();
  await apiBaseUrlInput.fill("https://checkout-test.adyen.com/v72");
}

/** Empty Standard webhook HMAC shows the required-length error. */
export async function verifyEmptyStandardHmacShowsError(page: Page): Promise<void> {
  // Empty value fails the min(1) base check first, before the Adyen
  // superRefine runs.
  const standardHmacInput = page.getByLabel("Standard webhook HMAC");
  await standardHmacInput.fill("");
  await standardHmacInput.blur();
  await expect(
    page
      .getByText("String must contain at least 1 character(s)", { exact: true })
      .first(),
  ).toBeVisible();
}

/** Invalid Standard webhook HMAC shows the exact hex error. */
export async function verifyInvalidStandardHmacShowsError(page: Page): Promise<void> {
  const standardHmacInput = page.getByLabel("Standard webhook HMAC");
  await standardHmacInput.fill("not-a-valid-hmac");
  await standardHmacInput.blur();
  await expect(
    page.getByText("Use the 64-character hexadecimal Adyen HMAC key.", { exact: true }),
  ).toBeVisible();
  await standardHmacInput.fill(
    "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  );
}

/** Invalid Adyen Token webhook HMAC shows the exact hex error. */
export async function verifyInvalidTokenHmacShowsError(page: Page): Promise<void> {
  const tokenHmacInput = page.getByLabel("Token webhook HMAC");
  await tokenHmacInput.fill("not-a-valid-token-hmac");
  await tokenHmacInput.blur();
  await expect(
    page.getByText(
      "Use the 64-character hexadecimal token-webhook HMAC key.",
      { exact: true },
    ),
  ).toBeVisible();
  await tokenHmacInput.fill(
    "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210",
  );
}

/**
 * Switching Provider to Stripe changes labels and hides Token webhook HMAC
 * and Checkout API base URL.
 */
export async function verifySwitchingToStripeChangesLabels(page: Page): Promise<void> {
  await page.getByRole("combobox", { name: "Provider" }).click();
  await page.getByRole("option", { name: "Stripe Checkout" }).click();

  await expect(page.getByLabel("Webhook endpoint secret")).toBeVisible();
  await expect(page.getByLabel("Standard webhook HMAC")).toHaveCount(0);
  await expect(page.getByLabel("Token webhook HMAC")).toHaveCount(0);
  await expect(
    page.getByRole("textbox", { name: "Checkout API base URL" }),
  ).toHaveCount(0);
}

/** A Stripe API key without sk_/rk_ shows the exact prefix error. */
export async function verifyStripeApiKeyWithoutPrefixShowsError(page: Page): Promise<void> {
  const apiKeyInput = page.getByLabel("API key");
  await apiKeyInput.fill("invalid-stripe-key");
  await apiKeyInput.blur();
  await expect(
    page.getByText("Stripe API keys start with sk_ or rk_.", { exact: true }),
  ).toBeVisible();
  await apiKeyInput.fill("sk_test_1234567890");
}

/** A Stripe webhook secret without whsec_ shows the exact prefix error. */
export async function verifyStripeWebhookSecretWithoutPrefixShowsError(
  page: Page,
): Promise<void> {
  const secretInput = page.getByLabel("Webhook endpoint secret");
  await secretInput.fill("invalid-secret");
  await secretInput.blur();
  await expect(
    page.getByText("Stripe endpoint secrets start with whsec_.", { exact: true }),
  ).toBeVisible();
  await secretInput.fill("whsec_test_1234567890");
}

/**
 * Security: no real provider is ever registered by this test
 * (Create provider is never submitted). The button is enabled but we
 * intentionally don't click it — it would register real, encrypted-at-rest
 * credentials against this tenant.
 */
export async function verifyCreateProviderButtonNeverSubmitted(page: Page): Promise<void> {
  const createProviderButton = page.getByRole("button", { name: "Create provider" });
  await expect(createProviderButton).toBeEnabled();
}

/** Cancel returns to the Payment Providers list without creating anything. */
export async function verifyCancelReturnsToListFromCreate(page: Page): Promise<void> {
  await page.getByRole("button", { name: "Cancel" }).click();
  await expect(
    page.getByRole("heading", { name: "Payment providers" }),
  ).toBeVisible();
  await expect(page.getByRole("table")).toBeVisible();
}

/** Row Edit action opens the Update page with provider identity section. */
export async function verifyRowEditOpensUpdatePage(page: Page): Promise<void> {
  await page.getByRole("link", { name: "Edit" }).first().click();
  await expect(
    page.getByRole("heading", { name: "Update payment provider" }),
  ).toBeVisible();
  // Identity section shows the provider display name + version badge + merchant id.
  await expect(
    page.getByRole("heading", { name: "Adyen Hosted Checkout" }).first(),
  ).toBeVisible();
  await expect(page.getByText("Version 1", { exact: true })).toBeVisible();
  await expect(page.getByText("Merchant: YourAdyenMerchant", { exact: true })).toBeVisible();
}

/** Update form is pre-filled from the provider record. */
export async function verifyUpdateFormPrefilled(page: Page): Promise<void> {
  await expect(
    page.getByRole("spinbutton", { name: "Maximum refund age" }),
  ).toHaveValue("365");
  await expect(
    page.getByRole("textbox", { name: "Frontend result URL" }),
  ).toHaveValue(/\/payment\/result$/);
  await expect(page.getByRole("textbox", { name: "Country code" })).toHaveValue("CH");
}

/** Update page exposes a Provider enabled switch (only on update). */
export async function verifyUpdatePageExposesEnabledSwitch(page: Page): Promise<void> {
  await expect(
    page.getByRole("switch", { name: "Enable payment provider" }),
  ).toBeVisible();
}

/** Concurrency protected card renders on the update page. */
export async function verifyConcurrencyProtectedCardRenders(page: Page): Promise<void> {
  await expect(
    page.getByRole("heading", { name: "Concurrency protected" }),
  ).toBeVisible();
}

/** Cancel from update returns to the list. */
export async function verifyCancelFromUpdateReturnsToList(page: Page): Promise<void> {
  await page.getByRole("button", { name: "Cancel" }).click();
  await expect(
    page.getByRole("heading", { name: "Payment providers" }),
  ).toBeVisible();
  await expect(page.getByRole("table")).toBeVisible();
}

/** Row Rotate action opens the Rotate page with provider identity section. */
export async function verifyRowRotateOpensRotatePage(page: Page): Promise<void> {
  await page.getByRole("link", { name: "Rotate" }).first().click();
  await expect(
    page.getByRole("heading", { name: "Rotate provider credentials" }),
  ).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "Adyen Hosted Checkout" }).first(),
  ).toBeVisible();
  await expect(page.getByText("Version 1", { exact: true })).toBeVisible();
  await expect(page.getByText("Merchant: YourAdyenMerchant", { exact: true })).toBeVisible();
}

/**
 * Rotate form is loaded with all credential fields empty. Security:
 * existing values are never loaded into the rotation form.
 */
export async function verifyRotateFormLoadedWithEmptyFields(page: Page): Promise<void> {
  await expect(page.getByRole("textbox", { name: "New API key" })).toHaveValue("");
  await expect(page.getByLabel("New standard webhook HMAC")).toHaveValue("");
  await expect(page.getByLabel("New token webhook HMAC")).toHaveValue("");
}

/** Webhook overlap card renders on the rotate page. */
export async function verifyWebhookOverlapCardRenders(page: Page): Promise<void> {
  await expect(
    page.getByRole("heading", { name: "Webhook overlap" }),
  ).toBeVisible();
}

/** Protected operation card renders on the rotate page. */
export async function verifyProtectedOperationCardRenders(page: Page): Promise<void> {
  await expect(
    page.getByRole("heading", { name: "Protected operation" }),
  ).toBeVisible();
}

/**
 * Submitting with all fields empty surfaces
 * 'Enter at least one credential to rotate.'
 */
export async function verifyRotateAllEmptySurfacesError(page: Page): Promise<void> {
  await page.getByRole("button", { name: "Rotate credentials" }).click();
  await expect(
    page.getByText("Enter at least one credential to rotate.", { exact: true }),
  ).toBeVisible();
}

/** Adyen rotate non-hex HMAC shows the exact error. */
export async function verifyAdyenRotateNonHexHmacShowsError(page: Page): Promise<void> {
  const hmacField = page.getByLabel("New standard webhook HMAC");
  await hmacField.fill("not-hex");
  // Move focus to trigger onBlur validation.
  await page.getByRole("button", { name: "Cancel" }).focus();
  await expect(
    page.getByText("Use a 64-character hexadecimal Adyen HMAC key.", { exact: true }),
  ).toBeVisible();
  await hmacField.fill("");
}

/** Cancel from rotate returns to the list. */
export async function verifyCancelFromAdyenRotateReturnsToList(page: Page): Promise<void> {
  await page.getByRole("button", { name: "Cancel" }).click();
  await expect(
    page.getByRole("heading", { name: "Payment providers" }),
  ).toBeVisible();
  await expect(page.getByRole("table")).toBeVisible();
}

/** Rotating the Stripe provider switches the labels to Stripe-specific copy. */
export async function verifyRotatingStripeProviderSwitchesLabels(page: Page): Promise<void> {
  await page.getByRole("link", { name: "Rotate" }).nth(1).click();
  await expect(
    page.getByRole("heading", { name: "Rotate provider credentials" }),
  ).toBeVisible();
  await expect(page.getByRole("textbox", { name: "New API key" })).toHaveValue("");
  await expect(page.getByLabel("New webhook endpoint secret")).toHaveValue("");
  // Stripe has no separate token webhook.
  await expect(page.getByLabel("New token webhook HMAC")).toHaveCount(0);
}

/** Stripe rotation rejects a token-webhook HMAC with the exact error. */
export async function verifyStripeRotationRejectsNonWhsecSecret(page: Page): Promise<void> {
  // The Stripe schema explicitly rejects any tokenHmacKey value. The
  // rotate form for Stripe does not expose a token field, so we can't
  // test it via the UI here — but we can confirm the Stripe secret
  // format is enforced when supplied.
  await page.getByLabel("New webhook endpoint secret").fill("not-a-whsec");
  await page.getByLabel("New webhook endpoint secret").blur();
  await expect(
    page.getByText("Stripe endpoint secrets start with whsec_.", { exact: true }),
  ).toBeVisible();
  await page.getByLabel("New webhook endpoint secret").fill("");
}

/** Cancel from Stripe rotate returns to the list. */
export async function verifyCancelFromStripeRotateReturnsToList(page: Page): Promise<void> {
  await page.getByRole("button", { name: "Cancel" }).click();
  await expect(
    page.getByRole("heading", { name: "Payment providers" }),
  ).toBeVisible();
  await expect(page.getByRole("table")).toBeVisible();
}
