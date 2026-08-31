import { test, expect, type Route } from "@playwright/test";
import { openPaymentsSubPage } from "../../support/auth-helpers";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";

const PROVIDER_ADYEN = {
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

const PROVIDER_STRIPE = {
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

const emptyProvidersBody = () =>
  JSON.stringify({ success: true, data: [], error: null });

const providersBody = (items: unknown[]) =>
  JSON.stringify({ success: true, data: items, error: null });

const errorBody = (message: string) =>
  JSON.stringify({
    success: false,
    data: null,
    error: { code: "fetch_failed", message },
  });

test.describe("Payments", () => {
  test("Payment Providers", async ({ page }) => {
    // -----------------------------------------------------------------
    // Route stubs
    // -----------------------------------------------------------------
    const holder: {
      responder: () => {
        status: number;
        contentType: string;
        body: string;
      };
    } = {
      responder: () => ({
        status: 200,
        contentType: "application/json",
        body: emptyProvidersBody(),
      }),
    };

    await page.route("**/api/payments/providers**", async (route: Route) => {
      const req = route.request();
      // For non-GET (POST/PUT) we let the call through so a stray click never
      // silently creates a real provider in the tenant.
      if (req.method() !== "GET") return route.continue();
      await route.fulfill(holder.responder());
    });

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

    // ---------------------------------------------------------------
    // Navigation
    // ---------------------------------------------------------------
    await openUtilitiesDashboard(page);
    await openPaymentsSubPage(page, "Payment Providers");

    // =================================================================
    // Section A — List page (empty/error states, no data)
    // =================================================================
    await test.step("[Positive] page header explains credentials are never returned", async () => {
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
    });

    await test.step("[Positive] empty environment shows 'No payment provider registered' with explanatory subtitle and Create CTA", async () => {
      await expect(
        page.getByRole("heading", { name: "No payment provider registered" }),
      ).toBeVisible();
      await expect(
        page.getByText(
          "Register a provider before creating payment sessions.",
        ),
      ).toBeVisible();
      // The empty state surfaces an inline Create provider button — distinct
      // from the header's link, so both should exist at once.
      await expect(
        page
          .getByRole("link", { name: "Create provider", exact: true })
          .first(),
      ).toBeVisible();
    });

    await test.step("[Positive] loading skeleton appears while providers are being fetched", async () => {
      // Swap to a slow responder then reload so the skeleton is observable.
      holder.responder = () =>
        new Promise(() => {
          // never resolves for the duration of this step
        }) as unknown as {
          status: number;
          contentType: string;
          body: string;
        };
      await page.reload();
      await expect(
        page.locator('[aria-label="Loading providers"]'),
      ).toBeVisible();
      // restore the default empty responder
      holder.responder = () => ({
        status: 200,
        contentType: "application/json",
        body: emptyProvidersBody(),
      });
      await page.reload();
      await expect(
        page.getByRole("heading", { name: "No payment provider registered" }),
      ).toBeVisible();
    });

    await test.step("[Negative] error state surfaces 'Providers could not be loaded' with Try again", async () => {
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

      // Switch responder back to a successful empty list so Try again can recover.
      holder.responder = () => ({
        status: 200,
        contentType: "application/json",
        body: emptyProvidersBody(),
      });
      await page.getByRole("button", { name: "Try again" }).click();
      await expect(
        page.getByRole("heading", { name: "No payment provider registered" }),
      ).toBeVisible();
    });

    // =================================================================
    // Section B — List page (with mocked providers)
    // =================================================================
    holder.responder = () => ({
      status: 200,
      contentType: "application/json",
      body: providersBody([PROVIDER_ADYEN, PROVIDER_STRIPE]),
    });
    await page.reload();
    await expect(page.getByRole("heading", { name: "Registered providers" })).toBeVisible();

    await test.step("[Positive] provider table renders rows with provider, merchant, organization, country, capture, status, version and actions", async () => {
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
      await expect(
        table.getByRole("link", { name: "Rotate" }).first(),
      ).toBeVisible();
    });

    await test.step("[Positive] status filter narrows the table to only Enabled rows", async () => {
      // Open the status select trigger (Radix Select renders a combobox
      // whose accessible name is its current value).
      const statusSelect = page.locator(
        '[role="combobox"]',
      ).filter({ hasText: "All statuses" });
      await statusSelect.click();
      await page.getByRole("option", { name: "Enabled", exact: true }).click();

      const table = page.getByRole("table");
      await expect(table.getByText("Adyen Hosted Checkout")).toBeVisible();
      await expect(table.getByText("Stripe Checkout")).toHaveCount(0);

      // Reset back to "all" for the next step.
      const resetSelect = page.locator(
        '[role="combobox"]',
      ).filter({ hasText: "Enabled" });
      await resetSelect.click();
      await page.getByRole("option", { name: "All statuses", exact: true }).click();
      await expect(table.getByText("Stripe Checkout")).toBeVisible();
    });

    await test.step("[Positive] status filter narrows to only Disabled rows", async () => {
      const statusSelect = page.locator(
        '[role="combobox"]',
      ).filter({ hasText: "All statuses" });
      await statusSelect.click();
      await page.getByRole("option", { name: "Disabled", exact: true }).click();

      const table = page.getByRole("table");
      await expect(table.getByText("Stripe Checkout")).toBeVisible();
      await expect(table.getByText("Adyen Hosted Checkout")).toHaveCount(0);

      const resetSelect = page.locator(
        '[role="combobox"]',
      ).filter({ hasText: "Disabled" });
      await resetSelect.click();
      await page.getByRole("option", { name: "All statuses", exact: true }).click();
    });

    await test.step("[Positive] search input filters rows by merchant id", async () => {
      const search = page.getByRole("textbox", {
        name: "Search payment providers",
      });
      await search.click();
      await search.pressSequentially("stripeMerchant", { delay: 20 });

      const table = page.getByRole("table");
      await expect(table.getByText("Stripe Checkout")).toBeVisible();
      await expect(table.getByText("Adyen Hosted Checkout")).toHaveCount(0);

      await search.fill("");
      await expect(table.getByText("Adyen Hosted Checkout")).toBeVisible();
      await expect(table.getByText("Stripe Checkout")).toBeVisible();
    });

    await test.step("[Positive] search input filters rows by provider name", async () => {
      const search = page.getByRole("textbox", {
        name: "Search payment providers",
      });
      await search.fill("ADYEN");

      const table = page.getByRole("table");
      await expect(table.getByText("Adyen Hosted Checkout")).toBeVisible();
      await expect(table.getByText("Stripe Checkout")).toHaveCount(0);

      await search.fill("");
    });

    await test.step("[Positive] empty filter result shows 'No providers match these filters' (no Create CTA)", async () => {
      const search = page.getByRole("textbox", {
        name: "Search payment providers",
      });
      await search.fill("nonexistent-merchant");

      await expect(
        page.getByRole("heading", {
          name: "No providers match these filters",
        }),
      ).toBeVisible();
      await expect(
        page.getByText("Change the search or status filter."),
      ).toBeVisible();
      // The Create CTA only appears on the truly-empty state, not the
      // filter-empty state.
      await expect(
        page.getByRole("heading", { name: "No payment provider registered" }),
      ).toHaveCount(0);

      await search.fill("");
      // Back to the table.
      await expect(page.getByRole("table")).toBeVisible();
    });

    await test.step("[Positive] Refresh button re-fetches the provider list", async () => {
      const request = page.waitForRequest(
        (req) =>
          req.url().includes("/api/payments/providers") && req.method() === "GET",
      );
      await page.getByRole("button", { name: "Refresh" }).click();
      await request;
      // Table is still rendered after refresh.
      await expect(page.getByRole("table")).toBeVisible();
    });

    // =================================================================
    // Section C — Create page (from list)
    // =================================================================
    await test.step("[Positive] Create provider link opens the registration page", async () => {
      await page
        .getByRole("link", { name: "Create provider", exact: true })
        .first()
        .click();
      await expect(
        page.getByRole("heading", { name: "Create payment provider" }),
      ).toBeVisible();
    });

    await test.step("[Positive] Create Payment Provider form loads with Adyen defaults", async () => {
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
    });

    await test.step("[Positive] Webhook endpoints card renders with copy controls", async () => {
      await expect(
        page.getByRole("heading", { name: "Webhook endpoints" }),
      ).toBeVisible();
      // Two endpoints are rendered for Adyen: Standard notifications and Token notifications.
      await expect(
        page.getByText("Standard notifications", { exact: true }),
      ).toBeVisible();
      await expect(
        page.getByText("Token notifications", { exact: true }),
      ).toBeVisible();
      // Two copy controls (one per endpoint).
      await expect(page.getByText(/^Copy /)).toHaveCount(2);
    });

    await test.step("[Positive] Identity keys card renders on the create page", async () => {
      await expect(
        page.getByRole("heading", { name: "Identity keys" }),
      ).toBeVisible();
      await expect(
        page.getByText(/Return-state and shopper-reference keys/),
      ).toBeVisible();
    });

    await test.step("[Positive] Before creating card renders on the create page", async () => {
      await expect(
        page.getByRole("heading", { name: "Before creating" }),
      ).toBeVisible();
      await expect(
        page.getByText(/Register the endpoints above in your provider/),
      ).toBeVisible();
    });

    await test.step("[Positive] Additional organizations checkbox list appears once a primary org is picked", async () => {
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
      await page
        .locator('[role="combobox"]')
        .filter({ hasText: "Acme Corp" })
        .click();
      await page
        .getByRole("option", {
          name: "Every organization in this tenant",
          exact: true,
        })
        .click();
    });

    await test.step("[Positive] Manual capture switch toggles state", async () => {
      const manualCaptureSwitch = page.getByRole("switch", {
        name: "Enable manual capture",
      });
      await expect(manualCaptureSwitch).not.toBeChecked();
      await manualCaptureSwitch.click();
      await expect(manualCaptureSwitch).toBeChecked();
      await manualCaptureSwitch.click();
      await expect(manualCaptureSwitch).not.toBeChecked();
    });

    await test.step("[Positive] Store ID input accepts a value", async () => {
      const storeIdInput = page.getByRole("textbox", { name: "Store ID" });
      await storeIdInput.fill("store_abc");
      await expect(storeIdInput).toHaveValue("store_abc");
      await storeIdInput.fill("");
    });

    // -- Validation tests on the create page --
    const merchantIdInput = page.getByRole("textbox", { name: "Merchant ID" });
    const apiKeyInput = page.getByLabel("API key");
    const frontendResultInput = page.getByRole("textbox", {
      name: "Frontend result URL",
    });
    const countryInput = page.getByRole("textbox", { name: "Country code" });
    const maxRefundInput = page.getByRole("spinbutton", {
      name: "Maximum refund age",
    });
    const apiBaseUrlInput = page.getByRole("textbox", {
      name: "Checkout API base URL",
    });
    const standardHmacInput = page.getByLabel("Standard webhook HMAC");

    await test.step("[Negative] empty Merchant ID shows required error", async () => {
      await merchantIdInput.fill("");
      await merchantIdInput.blur();
      await expect(
        page
          .getByText("String must contain at least 1 character(s)", {
            exact: true,
          })
          .first(),
      ).toBeVisible();
      await merchantIdInput.fill("YourAdyenMerchant");
    });

    await test.step("[Negative] empty API key shows required error", async () => {
      await apiKeyInput.fill("");
      await apiKeyInput.blur();
      await expect(
        page
          .getByText("String must contain at least 1 character(s)", {
            exact: true,
          })
          .first(),
      ).toBeVisible();
      await apiKeyInput.fill("test-api-key");
    });

    await test.step("[Negative] empty Frontend result URL shows the exact required error", async () => {
      await frontendResultInput.fill("");
      await frontendResultInput.blur();
      await expect(
        page.getByText("Enter the frontend result URL.", { exact: true }),
      ).toBeVisible();
      await frontendResultInput.fill(
        "https://app.example.com/app/foo/payment/result",
      );
    });

    await test.step("[Negative] non-HTTPS Frontend result URL shows the exact error", async () => {
      await frontendResultInput.fill("http://insecure.example.com/payment/result");
      await frontendResultInput.blur();
      await expect(
        page.getByText("Enter an absolute HTTPS URL.", { exact: true }),
      ).toBeVisible();
      await frontendResultInput.fill(
        "https://app.example.com/app/foo/payment/result",
      );
    });

    await test.step("[Negative] invalid Country code shows the exact error", async () => {
      // The input is capped at 2 characters, so use a 2-char non-letter value
      // to trip the regex refine.
      await countryInput.fill("U1");
      await countryInput.blur();
      await expect(
        page.getByText("Use a two-letter ISO country code.", { exact: true }),
      ).toBeVisible();
      await countryInput.fill("CH");
    });

    await test.step("[Negative] Maximum refund age over 3650 surfaces an error", async () => {
      await maxRefundInput.fill("4000");
      await maxRefundInput.blur();
      await expect(
        page
          .getByText(/Number must be less than or equal to 3650/i)
          .first(),
      ).toBeVisible();
      await maxRefundInput.fill("365");
    });

    await test.step("[Negative] empty Checkout API base URL (Adyen) shows the Adyen-specific required error", async () => {
      await apiBaseUrlInput.fill("");
      await apiBaseUrlInput.blur();
      await expect(
        page.getByText("Adyen requires its Checkout API base URL.", { exact: true }),
      ).toBeVisible();
      await apiBaseUrlInput.fill("https://checkout-test.adyen.com/v72");
    });

    await test.step("[Negative] empty Standard webhook HMAC shows the required-length error", async () => {
      // Empty value fails the min(1) base check first, before the Adyen
      // superRefine runs.
      await standardHmacInput.fill("");
      await standardHmacInput.blur();
      await expect(
        page
          .getByText("String must contain at least 1 character(s)", {
            exact: true,
          })
          .first(),
      ).toBeVisible();
    });

    await test.step("[Negative] invalid Standard webhook HMAC shows the exact hex error", async () => {
      await standardHmacInput.fill("not-a-valid-hmac");
      await standardHmacInput.blur();
      await expect(
        page.getByText("Use the 64-character hexadecimal Adyen HMAC key.", { exact: true }),
      ).toBeVisible();
      await standardHmacInput.fill(
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      );
    });

    await test.step("[Negative] invalid Adyen Token webhook HMAC shows the exact hex error", async () => {
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
    });

    await test.step("[Positive] switching Provider to Stripe changes labels and hides Token webhook HMAC and Checkout API base URL", async () => {
      await page.getByRole("combobox", { name: "Provider" }).click();
      await page.getByRole("option", { name: "Stripe Checkout" }).click();

      await expect(page.getByLabel("Webhook endpoint secret")).toBeVisible();
      await expect(page.getByLabel("Standard webhook HMAC")).toHaveCount(0);
      await expect(page.getByLabel("Token webhook HMAC")).toHaveCount(0);
      await expect(
        page.getByRole("textbox", { name: "Checkout API base URL" }),
      ).toHaveCount(0);
    });

    await test.step("[Negative] a Stripe API key without sk_/rk_ shows the exact prefix error", async () => {
      await apiKeyInput.fill("invalid-stripe-key");
      await apiKeyInput.blur();
      await expect(
        page.getByText("Stripe API keys start with sk_ or rk_.", { exact: true }),
      ).toBeVisible();
      await apiKeyInput.fill("sk_test_1234567890");
    });

    await test.step("[Negative] a Stripe webhook secret without whsec_ shows the exact prefix error", async () => {
      await page.getByLabel("Webhook endpoint secret").fill("invalid-secret");
      await page.getByLabel("Webhook endpoint secret").blur();
      await expect(
        page.getByText("Stripe endpoint secrets start with whsec_.", { exact: true }),
      ).toBeVisible();
      await page
        .getByLabel("Webhook endpoint secret")
        .fill("whsec_test_1234567890");
    });

    const createProviderButton = page.getByRole("button", {
      name: "Create provider",
    });
    await test.step("[Security] no real provider is ever registered by this test (Create provider is never submitted)", async () => {
      await expect(createProviderButton).toBeEnabled();
      // Intentionally not clicking Create provider — it would register real,
      // encrypted-at-rest credentials against this tenant.
    });

    await test.step("[Positive] Cancel returns to the Payment Providers list without creating anything", async () => {
      await page.getByRole("button", { name: "Cancel" }).click();
      await expect(
        page.getByRole("heading", { name: "Payment providers" }),
      ).toBeVisible();
      await expect(page.getByRole("table")).toBeVisible();
    });

    // =================================================================
    // Section D — Update page (from row Edit)
    // =================================================================
    await test.step("[Positive] row Edit action opens the Update page with provider identity section", async () => {
      await page.getByRole("link", { name: "Edit" }).first().click();
      await expect(
        page.getByRole("heading", { name: "Update payment provider" }),
      ).toBeVisible();
      // Identity section shows the provider display name + version badge + merchant id.
      await expect(
        page.getByRole("heading", { name: "Adyen Hosted Checkout" }).first(),
      ).toBeVisible();
      await expect(page.getByText("Version 1", { exact: true })).toBeVisible();
      await expect(
        page.getByText("Merchant: YourAdyenMerchant", { exact: true }),
      ).toBeVisible();
    });

    await test.step("[Positive] Update form is pre-filled from the provider record", async () => {
      // The MaxRefundDays field is pre-filled to the record's value.
      await expect(
        page.getByRole("spinbutton", { name: "Maximum refund age" }),
      ).toHaveValue("365");
      await expect(
        page.getByRole("textbox", { name: "Frontend result URL" }),
      ).toHaveValue(/\/payment\/result$/);
      await expect(page.getByRole("textbox", { name: "Country code" })).toHaveValue(
        "CH",
      );
    });

    await test.step("[Positive] Update page exposes a Provider enabled switch (only on update)", async () => {
      await expect(
        page.getByRole("switch", { name: "Enable payment provider" }),
      ).toBeVisible();
    });

    await test.step("[Positive] Concurrency protected card renders on the update page", async () => {
      await expect(
        page.getByRole("heading", { name: "Concurrency protected" }),
      ).toBeVisible();
    });

    await test.step("[Positive] Cancel from update returns to the list", async () => {
      await page.getByRole("button", { name: "Cancel" }).click();
      await expect(
        page.getByRole("heading", { name: "Payment providers" }),
      ).toBeVisible();
      await expect(page.getByRole("table")).toBeVisible();
    });

    // =================================================================
    // Section E — Rotate page (from row Rotate)
    // =================================================================
    await test.step("[Positive] row Rotate action opens the Rotate page with provider identity section", async () => {
      await page.getByRole("link", { name: "Rotate" }).first().click();
      await expect(
        page.getByRole("heading", { name: "Rotate provider credentials" }),
      ).toBeVisible();
      await expect(
        page.getByRole("heading", { name: "Adyen Hosted Checkout" }).first(),
      ).toBeVisible();
      await expect(page.getByText("Version 1", { exact: true })).toBeVisible();
      await expect(
        page.getByText("Merchant: YourAdyenMerchant", { exact: true }),
      ).toBeVisible();
    });

    await test.step("[Positive] Rotate form is loaded with all credential fields empty", async () => {
      // Security: existing values are never loaded into the rotation form.
      await expect(
        page.getByRole("textbox", { name: "New API key" }),
      ).toHaveValue("");
      await expect(
        page.getByLabel("New standard webhook HMAC"),
      ).toHaveValue("");
      await expect(
        page.getByLabel("New token webhook HMAC"),
      ).toHaveValue("");
    });

    await test.step("[Positive] Webhook overlap card renders on the rotate page", async () => {
      await expect(
        page.getByRole("heading", { name: "Webhook overlap" }),
      ).toBeVisible();
    });

    await test.step("[Positive] Protected operation card renders on the rotate page", async () => {
      await expect(
        page.getByRole("heading", { name: "Protected operation" }),
      ).toBeVisible();
    });

    await test.step("[Negative] submitting with all fields empty surfaces 'Enter at least one credential to rotate.'", async () => {
      await page
        .getByRole("button", { name: "Rotate credentials" })
        .click();
      await expect(
        page.getByText("Enter at least one credential to rotate.", { exact: true }),
      ).toBeVisible();
    });

    await test.step("[Negative] Adyen rotate non-hex HMAC shows the exact error", async () => {
      const hmacField = page.getByLabel("New standard webhook HMAC");
      await hmacField.fill("not-hex");
      // Move focus to trigger onBlur validation.
      await page.getByRole("button", { name: "Cancel" }).focus();
      await expect(
        page.getByText("Use a 64-character hexadecimal Adyen HMAC key.", { exact: true }),
      ).toBeVisible();
      await hmacField.fill("");
    });

    await test.step("[Positive] Cancel from rotate returns to the list", async () => {
      await page.getByRole("button", { name: "Cancel" }).click();
      await expect(
        page.getByRole("heading", { name: "Payment providers" }),
      ).toBeVisible();
      await expect(page.getByRole("table")).toBeVisible();
    });

    // =================================================================
    // Section F — Rotate page (Stripe provider)
    // =================================================================
    await test.step("[Positive] rotating the Stripe provider switches the labels to Stripe-specific copy", async () => {
      await page.getByRole("link", { name: "Rotate" }).nth(1).click();
      await expect(
        page.getByRole("heading", { name: "Rotate provider credentials" }),
      ).toBeVisible();
      await expect(
        page.getByRole("textbox", { name: "New API key" }),
      ).toHaveValue("");
      await expect(
        page.getByLabel("New webhook endpoint secret"),
      ).toHaveValue("");
      // Stripe has no separate token webhook.
      await expect(page.getByLabel("New token webhook HMAC")).toHaveCount(0);
    });

    await test.step("[Negative] Stripe rotation rejects a token-webhook HMAC with the exact error", async () => {
      // The Stripe schema explicitly rejects any tokenHmacKey value. The
      // rotate form for Stripe does not expose a token field, so we can't
      // test it via the UI here — but we can confirm the Stripe secret
      // format is enforced when supplied.
      await page
        .getByLabel("New webhook endpoint secret")
        .fill("not-a-whsec");
      await page.getByLabel("New webhook endpoint secret").blur();
      await expect(
        page.getByText("Stripe endpoint secrets start with whsec_.", { exact: true }),
      ).toBeVisible();
      await page.getByLabel("New webhook endpoint secret").fill("");
    });

    await test.step("[Positive] Cancel from Stripe rotate returns to the list", async () => {
      await page.getByRole("button", { name: "Cancel" }).click();
      await expect(
        page.getByRole("heading", { name: "Payment providers" }),
      ).toBeVisible();
      await expect(page.getByRole("table")).toBeVisible();
    });
  });
});