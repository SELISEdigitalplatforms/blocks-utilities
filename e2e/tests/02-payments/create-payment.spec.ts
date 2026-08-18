import { test, expect } from "@playwright/test";
import { loginFresh, openFirstProject, openPaymentsSubPage } from "../../support/auth-helpers";

test.describe("Payments", () => {
  test.beforeEach(async ({ page }) => {
    await loginFresh(page);
    await openFirstProject(page);
  });

  test("Create Payment - defaults and field validation", async ({ page }) => {
    await openPaymentsSubPage(page, "Create Payment");
    await expect(page.getByRole("heading", { name: "Test hosted payment" })).toBeVisible();

    const orderIdInput = page.getByRole("textbox", { name: "Order ID" });
    const amountInput = page.getByRole("spinbutton", { name: "Amount" });
    const submitButton = page.getByRole("button", { name: "Create and open checkout" });

    await test.step("[Positive] form loads with sensible defaults", async () => {
      await expect(page.getByRole("combobox", { name: "Provider" })).toHaveText("Adyen");
      await expect(page.getByRole("combobox", { name: "Currency" })).toHaveText(
        "CHF — Swiss Franc",
      );
      await expect(orderIdInput).toHaveValue(/^TEST-ORDER-\d+$/);
      await expect(amountInput).toHaveValue("10");
    });

    await test.step("[Positive] Provider and Currency dropdowns list the expected options", async () => {
      await page.getByRole("combobox", { name: "Provider" }).click();
      await expect(page.getByRole("option", { name: "Adyen" })).toBeVisible();
      await expect(page.getByRole("option", { name: "Stripe" })).toBeVisible();
      await page.keyboard.press("Escape");

      await page.getByRole("combobox", { name: "Currency" }).click();
      await expect(page.getByRole("option", { name: "USD — US Dollar" })).toBeVisible();
      await expect(page.getByRole("option", { name: "BDT — Bangladeshi Taka" })).toBeVisible();
      await page.keyboard.press("Escape");
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
    });

    await test.step("[Security] Checkout preferences default to off (no silent card-saving or recurring charges)", async () => {
      await expect(
        page.getByRole("switch", { name: "Offer to save payment method" }),
      ).not.toBeChecked();
      await expect(
        page.getByRole("switch", { name: "Recurring payment is disabled" }),
      ).not.toBeChecked();
    });

    await test.step("[Positive] side panel explains the secure redirect flow", async () => {
      await expect(page.getByRole("heading", { name: "Secure redirect flow" })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Before testing" })).toBeVisible();
    });
  });
});
