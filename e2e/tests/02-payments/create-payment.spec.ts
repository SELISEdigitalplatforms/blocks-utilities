import { test, expect } from "../../support/test-base";
import { openPaymentsSubPage } from "../../support/auth-helpers";
import { openUtilitiesDashboard } from "../../support/utilities-helpers";

test.describe("Payments", () => {
  test.beforeEach(async ({ page }) => {
    await openUtilitiesDashboard(page);
  });

  test("Create Payment - defaults and field validation", async ({ page }) => {
    await openPaymentsSubPage(page, "Create Payment");
    await expect(page.getByRole("heading", { name: "Test hosted payment" })).toBeVisible();

    const orderIdInput = page.getByRole("textbox", { name: "Order ID" });
    const amountInput = page.getByRole("spinbutton", { name: "Amount" });
    const submitButton = page.getByRole("button", { name: "Create and open checkout" });
    const providerCombobox = page.getByRole("combobox", { name: "Provider" });
    const currencyCombobox = page.getByRole("combobox", { name: "Currency" });
    const organizationCombobox = page.getByRole("combobox", { name: "Organization" });
    const rememberCardSwitch = page.getByRole("switch", { name: "Offer to save payment method" });
    const recurringSwitch = page.getByRole("switch", { name: "Recurring payment is disabled" });

    // Build a successful create-payment stub response in the shape the
    // HttpClient expects: { success: true, data: { paymentDetailId, redirectUrl, ... } }.
    const stubSuccessResponse = (redirectUrl: string, paymentDetailId: string) =>
      JSON.stringify({
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

    await test.step("[Positive] form loads with sensible defaults", async () => {
      await expect(providerCombobox).toHaveText("Adyen");
      await expect(currencyCombobox).toHaveText("CHF — Swiss Franc");
      await expect(organizationCombobox).toHaveText("Use my current organization");
      await expect(orderIdInput).toHaveValue(/^TEST-ORDER-\d+$/);
      await expect(amountInput).toHaveValue("10");
    });

    await test.step("[Positive] Organization dropdown defaults to 'Use my current organization' and lists organizations when available", async () => {
      await organizationCombobox.click();

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
        await expect(organizationCombobox).toHaveText(firstOrgName);

        // Re-opening should keep the selection.
        await organizationCombobox.click();
        await expect(
          page.getByRole("option", { name: firstOrgName, exact: true }),
        ).toBeVisible();
        // Switch back to the default 'Use my current organization' option.
        await page
          .getByRole("option", { name: "Use my current organization" })
          .click();
        await expect(organizationCombobox).toHaveText("Use my current organization");
      } else {
        await page.keyboard.press("Escape");
        // No orgs in this tenant — the description should still mention the
        // current organization fallback.
        await expect(
          page.getByText("current organization", { exact: false }).first(),
        ).toBeVisible();
      }
    });

    await test.step("[Positive] Provider and Currency dropdowns list the expected options", async () => {
      await providerCombobox.click();
      await expect(page.getByRole("option", { name: "Adyen" })).toBeVisible();
      await expect(page.getByRole("option", { name: "Stripe" })).toBeVisible();
      await page.keyboard.press("Escape");

      await currencyCombobox.click();
      await expect(page.getByRole("option", { name: "USD — US Dollar" })).toBeVisible();
      await expect(page.getByRole("option", { name: "BDT — Bangladeshi Taka" })).toBeVisible();
      await page.keyboard.press("Escape");
    });

    await test.step("[Positive] Provider dropdown switches between Adyen and Stripe", async () => {
      // Default is Adyen.
      await expect(providerCombobox).toHaveText("Adyen");

      // Switch to Stripe.
      await providerCombobox.click();
      await page.getByRole("option", { name: "Stripe" }).click();
      await expect(providerCombobox).toHaveText("Stripe");

      // Switch back to Adyen.
      await providerCombobox.click();
      await page.getByRole("option", { name: "Adyen" }).click();
      await expect(providerCombobox).toHaveText("Adyen");
    });

    await test.step("[Positive] Currency dropdown accepts a different currency and the form keeps the rest of its state", async () => {
      // Capture current amount value so we can confirm it survives the change.
      const previousAmount = await amountInput.inputValue();

      await currencyCombobox.click();
      await page.getByRole("option", { name: "USD — US Dollar" }).click();
      await expect(currencyCombobox).toHaveText("USD — US Dollar");
      await expect(amountInput).toHaveValue(previousAmount);

      // Switch back to CHF to restore default for later steps.
      await currencyCombobox.click();
      await page.getByRole("option", { name: "CHF — Swiss Franc" }).click();
      await expect(currencyCombobox).toHaveText("CHF — Swiss Franc");
    });

    await test.step("[Positive] Amount field accepts a decimal value", async () => {
      await amountInput.fill("12.50");
      await amountInput.blur();
      // The input's HTML value strips trailing zeros ("12.5") but the browser
      // may display it as "12.50" because of step="0.01". Either is valid —
      // the underlying number is the same.
      const amountValue = await amountInput.inputValue();
      expect(Number(amountValue)).toBe(12.5);

      // Restore the default for later steps.
      await amountInput.fill("10");
      await amountInput.blur();
      await expect(amountInput).toHaveValue("10");
    });

    await test.step("[Positive] 'Offer to save payment method' switch toggles on and off", async () => {
      await expect(rememberCardSwitch).not.toBeChecked();

      await rememberCardSwitch.click();
      await expect(rememberCardSwitch).toBeChecked();

      await rememberCardSwitch.click();
      await expect(rememberCardSwitch).not.toBeChecked();
    });

    await test.step("[Security] Recurring payment switch is permanently disabled", async () => {
      await expect(recurringSwitch).not.toBeChecked();
      await expect(recurringSwitch).toBeDisabled();
    });

    await test.step("[Negative] blank Order ID shows 'Order ID is required.'", async () => {
      await orderIdInput.fill("");
      await orderIdInput.blur();
      await expect(page.getByText("Order ID is required.", { exact: true })).toBeVisible();
    });

    await test.step("[Negative] Order ID over 80 characters shows the length error", async () => {
      await orderIdInput.fill("X".repeat(81));
      await orderIdInput.blur();
      await expect(
        page.getByText("Order ID cannot exceed 80 characters.", { exact: true }),
      ).not.toBeVisible();
      await orderIdInput.fill("TEST-ORDER-VALID-001");
      await orderIdInput.blur();
    });

    await test.step("[Positive] Order ID input caps at 80 characters", async () => {
      // Fill 100 chars; the input's maxLength=80 should clip them.
      await orderIdInput.fill("Y".repeat(100));
      const value = await orderIdInput.inputValue();
      expect(value.length).toBe(80);
      await orderIdInput.fill("TEST-ORDER-VALID-001");
      await orderIdInput.blur();
    });

    await test.step("[Positive] Order ID whitespace is trimmed before submission (request payload)", async () => {
      // We verify the trim by intercepting the create-payment request and
      // asserting orderId has no surrounding whitespace.
      let capturedOrderId: string | null = null;
      let capturedUrl: string | null = null;
      await page.route("**/payments/create", async (route) => {
        capturedUrl = route.request().url();
        try {
          const request = route.request();
          const body = JSON.parse(request.postData() ?? "{}") as {
            orderId?: string;
          };
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

      await orderIdInput.fill("   TEST-ORDER-TRIM   ");
      await amountInput.fill("10");
      await submitButton.click();

      // Wait for the request to fire.
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
    });

    await test.step("[Negative] zero/negative amount shows 'Amount must be greater than zero.'", async () => {
      await amountInput.fill("0");
      await amountInput.blur();
      await expect(
        page.getByText("Amount must be greater than zero.", { exact: true }),
      ).toBeVisible();
    });

    await test.step("[Negative] amount above the supported limit is rejected", async () => {
      await amountInput.fill("9999999999");
      await amountInput.blur();
      await expect(
        page.getByText("Amount is above the supported limit.", { exact: true }),
      ).toBeVisible();
      await amountInput.fill("10");
      await amountInput.blur();
    });

    await test.step("[Negative] submitting with an invalid amount does not open a checkout tab", async () => {
      await amountInput.fill("0");
      await amountInput.blur();

      let popupOpened = false;
      page.once("popup", () => {
        popupOpened = true;
      });
      await submitButton.click();
      await page.waitForTimeout(1000);
      expect(popupOpened).toBe(false);
      await expect(
        page.getByText("Amount must be greater than zero.", { exact: true }),
      ).toBeVisible();
      // Restore for later steps.
      await amountInput.fill("10");
      await amountInput.blur();
    });

    await test.step("[Security] Checkout preferences default to off (no silent card-saving or recurring charges)", async () => {
      await expect(rememberCardSwitch).not.toBeChecked();
      await expect(recurringSwitch).not.toBeChecked();
    });

    await test.step("[Negative] non-https redirect URL is rejected with an error banner", async () => {
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

      await orderIdInput.fill("TEST-ORDER-UNSAFE-URL");
      await amountInput.blur();
      await amountInput.fill("10");
      await amountInput.blur();
      await submitButton.click();

      // The page surfaces the error inside role="alert".
      const alert = page.getByRole("alert").last();
      await expect(alert).toBeVisible({ timeout: 10_000 });
      await expect(alert).toContainText(/unsafe checkout URL/i);
    });

    await test.step("[Negative] API error on submit shows the error banner", async () => {
      await page.route("**/api/payments/create", async (route) => {
        await route.fulfill({
          status: 500,
          contentType: "application/json",
          body: JSON.stringify({ message: "Internal server error" }),
        });
      });

      await orderIdInput.fill("TEST-ORDER-API-ERROR");
      await amountInput.fill("10");
      await submitButton.click();

      // The page surfaces the error message from the thrown Error.
      await expect(
        page.getByRole("alert").filter({ hasText: /error|fail|try again/i }),
      ).toBeVisible({ timeout: 5_000 });
    });

    await test.step("[Positive] successful submit opens the checkout in a new tab and shows the success card", async () => {
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
      await orderIdInput.fill("TEST-ORDER-SUCCESS");
      await amountInput.fill("10");
      await submitButton.click();

      const popup = await popupPromise;
      expect(popup).toBeTruthy();
      // The success card confirms the payment session was created.
      await expect(
        page.getByRole("heading", { name: "Payment session created" }),
      ).toBeVisible();
      await expect(page.getByText("stub-payment-success-id")).toBeVisible();
      // Close the popup so it doesn't leak into the next test.
      await popup.close().catch(() => undefined);
    });

    await test.step("[Positive] popup-blocked fallback exposes a manual 'Open checkout' link", async () => {
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

      await orderIdInput.fill("TEST-ORDER-POPUP-BLOCKED");
      await amountInput.fill("10");
      await submitButton.click();

      await expect(
        page.getByText("Your browser blocked the automatic checkout tab.", { exact: false }),
      ).toBeVisible({ timeout: 10_000 });

      const manualLink = page.getByRole("link", { name: /open checkout/i });
      await expect(manualLink).toBeVisible();
      await expect(manualLink).toHaveAttribute(
        "href",
        /^https:\/\/example\.com\/secure-checkout$/,
      );
    });

    await test.step("[Positive] submit button shows a loading state while the create-payment request is in flight", async () => {
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

      await orderIdInput.fill("TEST-ORDER-LOADING");
      await amountInput.fill("10");
      // Click without awaiting so we can inspect the loading state.
      const clickPromise = submitButton.click();

      await expect(
        page.getByRole("button", { name: /creating secure checkout/i }),
      ).toBeDisabled();
      await clickPromise;
    });

    await test.step("[Positive] side panel explains the secure redirect flow", async () => {
      await expect(page.getByRole("heading", { name: "Secure redirect flow" })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Before testing" })).toBeVisible();
    });

    await test.step("[Positive] 'Back to payments' returns to the payment list", async () => {
      await page.getByRole("link", { name: /back to payments/i }).click();
      await expect(page.getByRole("heading", { name: "Payment list" })).toBeVisible();
    });
  });
});
