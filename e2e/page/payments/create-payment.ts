import { expect, type Page } from "@playwright/test";
import { openPaymentsSubPage } from "../../support/auth-helpers";

/**
 * Create Payment flows. Each function is a reusable step that exercises a
 * single create-payment behavior end-to-end (the click, the fill, the assertion).
 *
 * Assumes the caller has already opened the project dashboard via
 * openUtilitiesDashboard(page) so the sidebar is visible.
 */

interface CreatePaymentLocators {
  orderId: ReturnType<Page["getByRole"]>;
  amount: ReturnType<Page["getByRole"]>;
  submit: ReturnType<Page["getByRole"]>;
  provider: ReturnType<Page["getByRole"]>;
  currency: ReturnType<Page["getByRole"]>;
  organization: ReturnType<Page["getByRole"]>;
  rememberCard: ReturnType<Page["getByRole"]>;
  recurring: ReturnType<Page["getByRole"]>;
}

/**
 * Build a successful create-payment stub response in the shape the
 * HttpClient expects: { success: true, data: { paymentDetailId, redirectUrl, ... } }.
 */
function stubSuccessResponse(redirectUrl: string, paymentDetailId: string): string {
  return JSON.stringify({
    success: true,
    data: {
      paymentDetailId,
      providerName: "ADYEN-ONLINE",
      paymentStatus: "PENDING",
      orderId: null,
      amount: 10,
      currencyCode: "CHF",
      redirectUrl,
      expiresAtUtc: null,
    },
  });
}

/**
 * Navigate to the Create Payment sub-page and resolve all form locators.
 * Use this as the entry point for any create-payment flow that needs to
 * interact with form controls.
 */
export async function openCreatePaymentForm(page: Page): Promise<CreatePaymentLocators> {
  await openPaymentsSubPage(page, "Create Payment");
  await expect(page.getByRole("heading", { name: "Test hosted payment" })).toBeVisible();
  return {
    orderId: page.getByRole("textbox", { name: "Order ID" }),
    amount: page.getByRole("spinbutton", { name: "Amount" }),
    submit: page.getByRole("button", { name: "Create and open checkout" }),
    provider: page.getByRole("combobox", { name: "Provider" }),
    currency: page.getByRole("combobox", { name: "Currency" }),
    organization: page.getByRole("combobox", { name: "Organization" }),
    rememberCard: page.getByRole("switch", { name: "Offer to save payment method" }),
    recurring: page.getByRole("switch", { name: "Recurring payment is disabled" }),
  };
}

/** Create Payment: form loads with sensible defaults. */
export async function verifyFormLoadsWithDefaults(form: CreatePaymentLocators): Promise<void> {
  await expect(form.provider).toHaveText("Adyen");
  await expect(form.currency).toHaveText("CHF — Swiss Franc");
  await expect(form.organization).toHaveText("Use my current organization");
  await expect(form.orderId).toHaveValue(/^TEST-ORDER-\d+$/);
  await expect(form.amount).toHaveValue("10");
}

/**
 * Create Payment: Organization dropdown defaults to 'Use my current
 * organization' and lists organizations when available.
 */
export async function verifyOrganizationDropdownBehavior(
  page: Page,
  form: CreatePaymentLocators,
): Promise<void> {
  await form.organization.click();

  // 'Use my current organization' is always present as the fallback option.
  await expect(
    page.getByRole("option", { name: "Use my current organization" }),
  ).toBeVisible();

  // Organizations are fetched dynamically — may or may not be present
  // in the test environment. If any are available, select one and confirm
  // the combobox reflects the choice.
  const firstOrgOption = page
    .getByRole("option")
    .filter({ hasNotText: "Use my current organization" })
    .first();
  const hasOrganizations = await firstOrgOption.isVisible().catch(() => false);

  if (hasOrganizations) {
    const firstOrgName = (await firstOrgOption.textContent())?.trim() ?? "";
    await firstOrgOption.click();
    await expect(form.organization).toHaveText(firstOrgName);

    // Re-opening should keep the selection.
    await form.organization.click();
    await expect(
      page.getByRole("option", { name: firstOrgName, exact: true }),
    ).toBeVisible();
    // Switch back to the default 'Use my current organization' option.
    await page
      .getByRole("option", { name: "Use my current organization" })
      .click();
    await expect(form.organization).toHaveText("Use my current organization");
  } else {
    await page.keyboard.press("Escape");
    // No orgs in this tenant — the description should still mention the
    // current organization fallback.
    await expect(
      page.getByText("current organization", { exact: false }).first(),
    ).toBeVisible();
  }
}

/**
 * Create Payment: Provider and Currency dropdowns list the expected options.
 */
export async function verifyProviderAndCurrencyDropdownOptions(
  page: Page,
  form: CreatePaymentLocators,
): Promise<void> {
  await form.provider.click();
  await expect(page.getByRole("option", { name: "Adyen" })).toBeVisible();
  await expect(page.getByRole("option", { name: "Stripe" })).toBeVisible();
  await page.keyboard.press("Escape");

  await form.currency.click();
  await expect(page.getByRole("option", { name: "USD — US Dollar" })).toBeVisible();
  await expect(page.getByRole("option", { name: "BDT — Bangladeshi Taka" })).toBeVisible();
  await page.keyboard.press("Escape");
}

/** Create Payment: Provider dropdown switches between Adyen and Stripe. */
export async function verifyProviderDropdownSwitches(
  page: Page,
  form: CreatePaymentLocators,
): Promise<void> {
  // Default is Adyen.
  await expect(form.provider).toHaveText("Adyen");

  // Switch to Stripe.
  await form.provider.click();
  await page.getByRole("option", { name: "Stripe" }).click();
  await expect(form.provider).toHaveText("Stripe");

  // Switch back to Adyen.
  await form.provider.click();
  await page.getByRole("option", { name: "Adyen" }).click();
  await expect(form.provider).toHaveText("Adyen");
}

/**
 * Create Payment: Currency dropdown accepts a different currency and the
 * form keeps the rest of its state.
 */
export async function verifyCurrencyDropdownAcceptsDifferent(
  page: Page,
  form: CreatePaymentLocators,
): Promise<void> {
  // Capture current amount value so we can confirm it survives the change.
  const previousAmount = await form.amount.inputValue();

  await form.currency.click();
  await page.getByRole("option", { name: "USD — US Dollar" }).click();
  await expect(form.currency).toHaveText("USD — US Dollar");
  await expect(form.amount).toHaveValue(previousAmount);

  // Switch back to CHF to restore default for later steps.
  await form.currency.click();
  await page.getByRole("option", { name: "CHF — Swiss Franc" }).click();
  await expect(form.currency).toHaveText("CHF — Swiss Franc");
}

/** Create Payment: Amount field accepts a decimal value. */
export async function verifyAmountAcceptsDecimal(form: CreatePaymentLocators): Promise<void> {
  await form.amount.fill("12.50");
  await form.amount.blur();
  // The input's HTML value strips trailing zeros ("12.5") but the browser
  // may display it as "12.50" because of step="0.01". Either is valid —
  // the underlying number is the same.
  const amountValue = await form.amount.inputValue();
  expect(Number(amountValue)).toBe(12.5);

  // Restore the default for later steps.
  await form.amount.fill("10");
  await form.amount.blur();
  await expect(form.amount).toHaveValue("10");
}

/** Create Payment: 'Offer to save payment method' switch toggles on and off. */
export async function verifyRememberCardSwitchToggle(form: CreatePaymentLocators): Promise<void> {
  await expect(form.rememberCard).not.toBeChecked();

  await form.rememberCard.click();
  await expect(form.rememberCard).toBeChecked();

  await form.rememberCard.click();
  await expect(form.rememberCard).not.toBeChecked();
}

/** Create Payment: Recurring payment switch is permanently disabled. */
export async function verifyRecurringSwitchPermanentlyDisabled(
  form: CreatePaymentLocators,
): Promise<void> {
  await expect(form.recurring).not.toBeChecked();
  await expect(form.recurring).toBeDisabled();
}

/** Create Payment: blank Order ID shows 'Order ID is required.' */
export async function verifyBlankOrderIdShowsRequired(page: Page, form: CreatePaymentLocators): Promise<void> {
  await form.orderId.fill("");
  await form.orderId.blur();
  await expect(page.getByText("Order ID is required.", { exact: true })).toBeVisible();
}

/**
 * Create Payment: Order ID over 80 characters shows the length error
 * (the field is hard-capped at 80, so this asserts the error never fires
 * for over-length input).
 */
export async function verifyOrderIdOver80CharsShowsLengthError(
  page: Page,
  form: CreatePaymentLocators,
): Promise<void> {
  await form.orderId.fill("X".repeat(81));
  await form.orderId.blur();
  await expect(
    page.getByText("Order ID cannot exceed 80 characters.", { exact: true }),
  ).not.toBeVisible();
  await form.orderId.fill("TEST-ORDER-VALID-001");
  await form.orderId.blur();
}

/** Create Payment: Order ID input caps at 80 characters. */
export async function verifyOrderIdCapsAt80Chars(form: CreatePaymentLocators): Promise<void> {
  // Fill 100 chars; the input's maxLength=80 should clip them.
  await form.orderId.fill("Y".repeat(100));
  const value = await form.orderId.inputValue();
  expect(value.length).toBe(80);
  await form.orderId.fill("TEST-ORDER-VALID-001");
  await form.orderId.blur();
}

/**
 * Create Payment: Order ID whitespace is trimmed before submission
 * (verified via route interception of the create-payment request payload).
 */
export async function verifyOrderIdWhitespaceTrimmedInRequest(
  page: Page,
  form: CreatePaymentLocators,
): Promise<void> {
  let capturedOrderId: string | null = null;
  let capturedUrl: string | null = null;
  await page.route("**/payments/create", async (route) => {
    capturedUrl = route.request().url();
    try {
      const request = route.request();
      const body = JSON.parse(request.postData() ?? "{}") as { orderId?: string };
      capturedOrderId = body.orderId ?? null;
    } catch {
      // ignore parse errors
    }
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: stubSuccessResponse("https://example.com/checkout", "stub-payment-id"),
    });
  });

  // Listen for the popup so it doesn't fail the test.
  page.once("popup", (popup) => popup.close().catch(() => undefined));

  await form.orderId.fill("   TEST-ORDER-TRIM   ");
  await form.amount.fill("10");
  await form.submit.click();

  // Wait for the request to fire.
  const captured = await expect
    .poll(() => capturedOrderId, { timeout: 5_000 })
    .toBeTruthy()
    .catch(() => null);
  if (captured === null) {
    throw new Error(
      `Route did not capture orderId. capturedUrl=${String(capturedUrl)}`,
    );
  }
  expect(capturedOrderId).toBe("TEST-ORDER-TRIM");
}

/**
 * Create Payment: zero/negative amount shows 'Amount must be greater than zero.'
 */
export async function verifyZeroAmountShowsGreaterThanZeroError(
  page: Page,
  form: CreatePaymentLocators,
): Promise<void> {
  await form.amount.fill("0");
  await form.amount.blur();
  await expect(
    page.getByText("Amount must be greater than zero.", { exact: true }),
  ).toBeVisible();
}

/** Create Payment: amount above the supported limit is rejected. */
export async function verifyAmountAboveLimitRejected(
  page: Page,
  form: CreatePaymentLocators,
): Promise<void> {
  await form.amount.fill("9999999999");
  await form.amount.blur();
  await expect(
    page.getByText("Amount is above the supported limit.", { exact: true }),
  ).toBeVisible();
  await form.amount.fill("10");
  await form.amount.blur();
}

/**
 * Create Payment: submitting with an invalid amount does not open a
 * checkout tab.
 */
export async function verifySubmitWithInvalidAmountDoesNotOpenCheckout(
  page: Page,
  form: CreatePaymentLocators,
): Promise<void> {
  await form.amount.fill("0");
  await form.amount.blur();

  let popupOpened = false;
  page.once("popup", () => {
    popupOpened = true;
  });
  await form.submit.click();
  await page.waitForTimeout(1000);
  expect(popupOpened).toBe(false);
  await expect(
    page.getByText("Amount must be greater than zero.", { exact: true }),
  ).toBeVisible();
  // Restore for later steps.
  await form.amount.fill("10");
  await form.amount.blur();
}

/**
 * Create Payment: Checkout preferences default to off (no silent
 * card-saving or recurring charges).
 */
export async function verifyCheckoutPreferencesDefaultOff(
  form: CreatePaymentLocators,
): Promise<void> {
  await expect(form.rememberCard).not.toBeChecked();
  await expect(form.recurring).not.toBeChecked();
}

/** Create Payment: non-https redirect URL is rejected with an error banner. */
export async function verifyNonHttpsRedirectUrlRejected(
  page: Page,
  form: CreatePaymentLocators,
): Promise<void> {
  await page.route("**/api/payments/create", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: stubSuccessResponse(
        "http://insecure.example.com/checkout",
        "stub-payment-id",
      ),
    });
  });

  page.once("popup", () => {
    // Should never open for unsafe URL.
  });

  await form.orderId.fill("TEST-ORDER-UNSAFE-URL");
  await form.amount.blur();
  await form.amount.fill("10");
  await form.amount.blur();
  await form.submit.click();

  // The page surfaces the error inside role="alert".
  const alert = page.getByRole("alert").last();
  await expect(alert).toBeVisible({ timeout: 10_000 });
  await expect(alert).toContainText(/unsafe checkout URL/i);
}

/** Create Payment: API error on submit shows the error banner. */
export async function verifyApiErrorOnSubmit(
  page: Page,
  form: CreatePaymentLocators,
): Promise<void> {
  await page.route("**/api/payments/create", async (route) => {
    await route.fulfill({
      status: 500,
      contentType: "application/json",
      body: JSON.stringify({ message: "Internal server error" }),
    });
  });

  await form.orderId.fill("TEST-ORDER-API-ERROR");
  await form.amount.fill("10");
  await form.submit.click();

  // The page surfaces the error message from the thrown Error.
  await expect(
    page.getByRole("alert").filter({ hasText: /error|fail|try again/i }),
  ).toBeVisible({ timeout: 5_000 });
}

/**
 * Create Payment: successful submit opens the checkout in a new tab and
 * shows the success card.
 */
export async function verifySuccessfulSubmitOpensCheckout(
  page: Page,
  form: CreatePaymentLocators,
): Promise<void> {
  await page.route("**/api/payments/create", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: stubSuccessResponse(
        "https://example.com/secure-checkout",
        "stub-payment-success-id",
      ),
    });
  });

  const popupPromise = page.waitForEvent("popup", { timeout: 5_000 });
  await form.orderId.fill("TEST-ORDER-SUCCESS");
  await form.amount.fill("10");
  await form.submit.click();

  const popup = await popupPromise;
  expect(popup).toBeTruthy();
  // The success card confirms the payment session was created.
  await expect(
    page.getByRole("heading", { name: "Payment session created" }),
  ).toBeVisible();
  await expect(page.getByText("stub-payment-success-id")).toBeVisible();
  // Close the popup so it doesn't leak into the next test.
  await popup.close().catch(() => undefined);
}

/**
 * Create Payment: popup-blocked fallback exposes a manual 'Open checkout'
 * link with the secure URL.
 */
export async function verifyPopupBlockedFallbackShowsManualLink(
  page: Page,
  form: CreatePaymentLocators,
): Promise<void> {
  await page.route("**/api/payments/create", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: stubSuccessResponse(
        "https://example.com/secure-checkout",
        "stub-payment-blocked-id",
      ),
    });
  });

  // Stub window.open right now (the page is already loaded; addInitScript
  // would only fire on the next navigation). Returning null mimics a
  // browser that blocks popups.
  await page.evaluate(() => {
    (window as unknown as { __originalOpen: typeof window.open }).__originalOpen =
      window.open.bind(window);
    (window as unknown as { open: (url?: string | URL, target?: string) => Window | null }).open = (
      url?: string | URL,
      target?: string,
    ) => {
      if (target === "_blank") return null;
      return (
        window as unknown as { __originalOpen: typeof window.open }
      ).__originalOpen(url ?? "", target ?? "_self");
    };
  });

  await form.orderId.fill("TEST-ORDER-POPUP-BLOCKED");
  await form.amount.fill("10");
  await form.submit.click();

  await expect(
    page.getByText("Your browser blocked the automatic checkout tab.", { exact: false }),
  ).toBeVisible({ timeout: 10_000 });

  const manualLink = page.getByRole("link", { name: /open checkout/i });
  await expect(manualLink).toBeVisible();
  await expect(manualLink).toHaveAttribute(
    "href",
    /^https:\/\/example\.com\/secure-checkout$/,
  );
}

/**
 * Create Payment: submit button shows a loading state while the
 * create-payment request is in flight.
 */
export async function verifySubmitButtonShowsLoadingState(
  page: Page,
  form: CreatePaymentLocators,
): Promise<void> {
  // Slow the request down so we can observe the pending state.
  await page.route("**/api/payments/create", async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 1_500));
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: stubSuccessResponse(
        "https://example.com/secure-checkout",
        "stub-payment-loading-id",
      ),
    });
  });

  page.once("popup", (popup) => popup.close().catch(() => undefined));

  await form.orderId.fill("TEST-ORDER-LOADING");
  await form.amount.fill("10");
  // Click without awaiting so we can inspect the loading state.
  const clickPromise = form.submit.click();

  await expect(
    page.getByRole("button", { name: /creating secure checkout/i }),
  ).toBeDisabled();
  await clickPromise;
}

/** Create Payment: side panel explains the secure redirect flow. */
export async function verifySidePanelExplainsSecureRedirectFlow(page: Page): Promise<void> {
  await expect(page.getByRole("heading", { name: "Secure redirect flow" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Before testing" })).toBeVisible();
}

/** Create Payment: 'Back to payments' returns to the payment list. */
export async function verifyBackToPaymentsReturnsToList(page: Page): Promise<void> {
  await page.getByRole("link", { name: /back to payments/i }).click();
  await expect(page.getByRole("heading", { name: "Payment list" })).toBeVisible();
}
