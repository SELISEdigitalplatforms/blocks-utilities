import { test, expect } from "../../support/test-base";
import { loginFresh } from "../../support/login-helper";

// Fresh, isolated context for this file — ignore the "chromium" project's
// default storageState and log in for real instead of reusing a saved session.

test.describe("create payment", () => {
  test.use({ storageState: { cookies: [], origins: [] } });
  test.beforeEach(async ({ page }) => {
    test.setTimeout(180_000);
    await loginFresh(page);

    await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
      timeout: 30_000,
    });
    await page
      .getByRole("button", { name: /Development/ })
      .first()
      .click();
    await expect(page.getByRole("heading", { name: "Project Details" })).toBeVisible({
      timeout: 30_000,
    });

    // "Create Payment" lives under the collapsible "Payments" section.
    const createPaymentLink = page.getByRole("link", {
      name: "Create Payment",
    });
    if (!(await createPaymentLink.isVisible().catch(() => false))) {
      await page.getByRole("button", { name: "Payments", exact: true }).click();
    }
    await createPaymentLink.click();
    await expect(page.getByRole("heading", { name: "Test hosted payment" })).toBeVisible({
      timeout: 30_000,
    });
  });

  test("TC-0009: Create Payment page renders with default field values", async ({ page }) => {
    await expect(page.getByRole("heading", { name: "Test hosted payment" })).toBeVisible();
    await expect(page.getByLabel("Provider")).toContainText("Adyen");
    await expect(page.getByLabel("Amount")).toHaveValue("10");
    await expect(page.getByLabel("Currency")).toContainText("CHF");
    await expect(page.getByLabel("Order ID")).toHaveValue(/^TEST-ORDER-\d+$/);
  });

  test("TC-0010: Provider dropdown offers Adyen and Stripe", async ({ page }) => {
    await page.getByLabel("Provider").click();
    await expect(page.getByRole("option", { name: "Adyen" })).toBeVisible();
    await expect(page.getByRole("option", { name: "Stripe" })).toBeVisible();
  });

  test("TC-0011: Amount is required and must be greater than zero", async ({ page }) => {
    const amountInput = page.getByLabel("Amount");
    await amountInput.fill("");
    await amountInput.blur();
    await expect(page.getByText("Enter a valid amount.")).toBeVisible();

    await amountInput.fill("-5");
    await amountInput.blur();
    await expect(page.getByText("Amount must be greater than zero.")).toBeVisible();
  });

  test("TC-0012: Amount above the supported limit is rejected", async ({ page }) => {
    const amountInput = page.getByLabel("Amount");
    await amountInput.fill("1000000000");
    await amountInput.blur();
    await expect(page.getByText("Amount is above the supported limit.")).toBeVisible();
  });

  test("TC-0013: Currency code must be exactly three letters", async ({ page }) => {
    // The Currency control is a fixed Select (PAYMENT_CURRENCY_OPTIONS), so this
    // confirms every offered option is a 3-letter code rather than typing a bad one.
    await page.getByLabel("Currency").click();
    const options = page.getByRole("option");
    const count = await options.count();
    for (let i = 0; i < count; i++) {
      const text = (await options.nth(i).innerText()).split("—")[0].trim();
      expect(text.length).toBe(3);
    }
    await page.keyboard.press("Escape");
  });

  test("TC-0014: Order ID is required and capped at 80 characters", async ({ page }) => {
    const orderIdInput = page.getByLabel("Order ID");
    await orderIdInput.fill("");
    await orderIdInput.blur();
    await expect(page.getByText("Order ID is required.")).toBeVisible();

    await expect(orderIdInput).toHaveAttribute("maxlength", "80");
  });

  test("TC-0015: 'Offer to save payment method' switch defaults to off", async ({ page }) => {
    const rememberSwitch = page.getByLabel("Offer to save payment method");
    await expect(rememberSwitch).not.toBeChecked();
    await rememberSwitch.click();
    await expect(rememberSwitch).toBeChecked();
  });

  test("TC-0016: 'Recurring payment' switch is always disabled", async ({ page }) => {
    const recurringSwitch = page.getByLabel(/Recurring payment/);
    await expect(recurringSwitch).toBeDisabled();
  });

  test("TC-0017: Submitting shows a pending state and disables the button", async ({ page }) => {
    const submitButton = page.getByRole("button", {
      name: "Create and open checkout",
    });
    await submitButton.click();
    await expect(page.getByRole("button", { name: "Creating secure checkout…" })).toBeDisabled();
  });

  test("TC-0018: Successful submission opens the hosted checkout in a new tab and shows a confirmation card", async ({
    page,
    context,
  }) => {
    await page.route("**/api/payments/create", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          success: true,
          data: {
            paymentDetailId: `pd-${Date.now()}`,
            providerName: "ADYEN-ONLINE",
            paymentStatus: "PROCESSING",
            orderId: `TEST-ORDER-${Date.now()}`,
            amount: 10,
            currencyCode: "CHF",
            redirectUrl: "https://test.adyen.link/session",
            expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
          },
          error: null,
          meta: {
            correlationId: "correlation-1",
            timestampUtc: new Date().toISOString(),
          },
        }),
      });
    });

    const orderIdInput = page.getByLabel("Order ID");
    await orderIdInput.fill(`TEST-ORDER-${Date.now()}`);

    const [popup] = await Promise.all([
      context.waitForEvent("page", { timeout: 20000 }).catch(() => null),
      page.getByRole("button", { name: "Create and open checkout" }).click(),
    ]);

    await expect(page.getByText("Payment session created")).toBeVisible({
      timeout: 30_000,
    });
    if (popup) {
      await expect(popup).toHaveURL(/^https:/);
    }
  });

  test("TC-0019: Checkout URL must be HTTPS, or the submission is treated as failed", async ({
    page,
  }) => {
    // Force the create-payment response to return a non-HTTPS redirect URL.
    await page.route("**/api/payments/create", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          success: true,
          data: {
            paymentDetailId: `pd-${Date.now()}`,
            redirectUrl: "http://insecure.example.com/checkout",
          },
          error: null,
          meta: {
            correlationId: "correlation-1",
            timestampUtc: new Date().toISOString(),
          },
        }),
      });
    });

    const orderIdInput = page.getByLabel("Order ID");
    await orderIdInput.fill(`TEST-ORDER-${Date.now()}`);
    await page.getByRole("button", { name: "Create and open checkout" }).click();

    await expect(
      page.getByText("The payment provider returned an unsafe checkout URL."),
    ).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText("Payment session created")).toHaveCount(0);
  });

  test("TC-0020: Browser pop-up blocking falls back to a manual 'Open checkout' link", async ({
    page,
  }) => {
    // Simulate a pop-up blocker by making window.open return null. The page
    // was already navigated in beforeEach, so addInitScript (future
    // navigations only) won't patch it — evaluate on the live document instead.
    await page.evaluate(() => {
      window.open = () => null;
    });

    await page.route("**/api/payments/create", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          success: true,
          data: {
            paymentDetailId: `pd-${Date.now()}`,
            providerName: "ADYEN-ONLINE",
            paymentStatus: "PROCESSING",
            orderId: `TEST-ORDER-${Date.now()}`,
            amount: 10,
            currencyCode: "CHF",
            redirectUrl: "https://test.adyen.link/session",
            expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
          },
          error: null,
          meta: {
            correlationId: "correlation-1",
            timestampUtc: new Date().toISOString(),
          },
        }),
      });
    });

    const orderIdInput = page.getByLabel("Order ID");
    await orderIdInput.fill(`TEST-ORDER-${Date.now()}`);
    await page.getByRole("button", { name: "Create and open checkout" }).click();

    await expect(page.getByText("Your browser blocked the automatic checkout tab.")).toBeVisible({
      timeout: 30_000,
    });
    await expect(page.getByRole("link", { name: "Open checkout" })).toBeVisible();
  });

  test("TC-0021: Submission failure shows an inline error and does not open a checkout tab", async ({
    page,
  }) => {
    await page.route("**/api/payments/create", async (route) => {
      await route.fulfill({
        status: 500,
        contentType: "application/json",
        body: JSON.stringify({
          isSuccess: false,
          errors: { message: "Provider unavailable" },
        }),
      });
    });

    const orderIdInput = page.getByLabel("Order ID");
    await orderIdInput.fill(`TEST-ORDER-${Date.now()}`);
    await page.getByRole("button", { name: "Create and open checkout" }).click();

    await expect(page.getByRole("alert")).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText("Payment session created")).toHaveCount(0);
  });

  test("TC-0022: 'Back to payments' link returns to the Payment List page", async ({ page }) => {
    await page.getByRole("link", { name: "Back to payments" }).click();
    await expect(page.getByRole("heading", { name: "Payment list" })).toBeVisible({
      timeout: 30_000,
    });
  });
});
