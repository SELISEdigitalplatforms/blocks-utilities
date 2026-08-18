import { test, expect } from "@playwright/test";
import { loginFresh, openFirstProject, openPaymentsSubPage } from "../../support/auth-helpers";

test.describe("Payments", () => {
  test.beforeEach(async ({ page }) => {
    await loginFresh(page);
    await openFirstProject(page);
  });

  test("Payment Providers", async ({ page }) => {
    await openPaymentsSubPage(page, "Payment Providers");

    await test.step("[Positive] page header explains credentials are never returned", async () => {
      await expect(page.getByRole("heading", { name: "Payment providers" })).toBeVisible();
      await expect(
        page.getByText("Credentials are never returned by this endpoint."),
      ).toBeVisible();
    });

    await test.step("[Positive] empty environment shows 'No payment provider registered'", async () => {
      const emptyState = page.getByText("No payment provider registered", { exact: true });
      if (await emptyState.isVisible().catch(() => false)) {
        await expect(
          page.getByText("Register a provider before creating payment sessions."),
        ).toBeVisible();
      }
    });

    await test.step("[Positive] Create provider opens the provider registration page", async () => {
      await page.getByRole("link", { name: "Create provider", exact: true }).click();
      await expect(page.getByRole("heading", { name: "Create payment provider" })).toBeVisible();
    });

    const merchantIdInput = page.getByRole("textbox", { name: "Merchant ID" });
    const apiKeyInput = page.getByLabel("API key");
    const createProviderButton = page.getByRole("button", { name: "Create provider" });

    await test.step("[Positive] Create Payment Provider form loads with Adyen defaults", async () => {
      await expect(page.getByRole("combobox", { name: "Provider" })).toHaveText(
        "Adyen Hosted Checkout",
      );
      await expect(page.getByRole("textbox", { name: "Checkout API base URL" })).toHaveValue(
        "https://checkout-test.adyen.com/v72",
      );
      await expect(page.getByRole("textbox", { name: "Frontend result URL" })).toHaveValue(
        /\/payment\/result$/,
      );
      await expect(page.getByRole("spinbutton", { name: "Maximum refund age" })).toHaveValue("365");
    });

    await test.step("[Negative] an invalid Adyen webhook HMAC shows the exact hex-format error", async () => {
      await merchantIdInput.fill("YourAdyenMerchant");
      await apiKeyInput.fill("test-api-key");
      await page.getByLabel("Standard webhook HMAC").fill("not-a-valid-hmac");
      await page.getByLabel("Standard webhook HMAC").blur();
      await expect(
        page.getByText("Use the 64-character hexadecimal Adyen HMAC key.", { exact: true }),
      ).toBeVisible();
    });

    await test.step("[Positive] switching Provider to Stripe changes labels and hides Token webhook HMAC", async () => {
      await page.getByRole("combobox", { name: "Provider" }).click();
      await page.getByRole("option", { name: "Stripe Checkout" }).click();

      await expect(page.getByLabel("Webhook endpoint secret")).toBeVisible();
      await expect(page.getByLabel("Standard webhook HMAC")).toHaveCount(0);
      await expect(page.getByLabel("Token webhook HMAC")).toHaveCount(0);
      await expect(page.getByRole("textbox", { name: "Checkout API base URL" })).toHaveCount(0);
    });

    await test.step("[Negative] a Stripe API key without sk_/rk_ shows the exact prefix error", async () => {
      await apiKeyInput.fill("invalid-stripe-key");
      await apiKeyInput.blur();
      await expect(
        page.getByText("Stripe API keys start with sk_ or rk_.", { exact: true }),
      ).toBeVisible();
    });

    await test.step("[Negative] a Stripe webhook secret without whsec_ shows the exact prefix error", async () => {
      await page.getByLabel("Webhook endpoint secret").fill("invalid-secret");
      await page.getByLabel("Webhook endpoint secret").blur();
      await expect(
        page.getByText("Stripe endpoint secrets start with whsec_.", { exact: true }),
      ).toBeVisible();
    });

    await test.step("[Security] no real provider is ever registered by this test (Create provider is never submitted)", async () => {
      await expect(createProviderButton).toBeEnabled();
      // Intentionally not clicking Create provider - it would register real,
      // encrypted-at-rest credentials against this tenant.
    });

    await test.step("[Positive] Cancel returns to the Payment Providers list without creating anything", async () => {
      await page.getByRole("button", { name: "Cancel" }).click();
      await expect(page.getByRole("heading", { name: "Payment providers" })).toBeVisible();
    });
  });
});
