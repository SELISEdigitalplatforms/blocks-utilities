import { expect, type Page } from "@playwright/test";
import { openUtilitiesSubscription } from "../../support/utilities-helpers";

/**
 * Offline billing (spec 006): a tenant admin activates and re-prices subscriptions for
 * organizations that pay by bank transfer. Each function is one step of that flow.
 */

export async function openOfflineBilling(page: Page): Promise<void> {
  await openUtilitiesSubscription(page, "Offline billing");
  await expect(page).toHaveURL(/\/subscription\/offline/);
  await expect(page.getByRole("heading", { name: "Offline billing" })).toBeVisible();
}

/**
 * Picks the organization named by `E2E_OFFLINE_ORGANIZATION`, or the first one listed. Returns
 * false when the project has no organization to activate for, so the caller can skip honestly.
 */
export async function chooseOrganization(page: Page): Promise<boolean> {
  await expect(page.getByText("Choose the customer organization the contract is for.")).toBeVisible();
  await page.getByRole("combobox", { name: "Organization" }).click();

  const wanted = process.env.E2E_OFFLINE_ORGANIZATION?.trim();
  const option = wanted
    ? page.getByRole("option", { name: wanted, exact: true })
    : page.getByRole("option").first();
  if (!(await option.isVisible({ timeout: 10_000 }).catch(() => false))) {
    await page.keyboard.press("Escape");
    return false;
  }

  await option.click();
  await expect(page).toHaveURL(/organizationId=/);
  return true;
}

export type OrganizationState = "activatable" | "offline" | "card";

/** Which of the three things the page offers for this organization. */
export async function organizationState(page: Page): Promise<OrganizationState> {
  const form = page.getByTestId("offline-activation-form");
  const offline = page.getByTestId("offline-billing-current").getByTestId("billed-by-invoice");
  const card = page.getByText(/This organization pays by card/);
  await expect(form.or(offline).or(card)).toBeVisible({ timeout: 20_000 });

  if (await form.isVisible()) return "activatable";
  return (await offline.isVisible()) ? "offline" : "card";
}

/**
 * Activates the first plan at a contract price. Returns false when the page reports that the
 * organization's billing profile, or the tenant's seller profile, is incomplete instead of
 * activating: every record invoice names both, so the server refuses without them.
 */
export async function activateAtContractPrice(page: Page, amount: string): Promise<boolean> {
  await page.getByRole("combobox", { name: "Plan" }).click();
  await page.getByRole("option").first().click();
  await page.getByRole("combobox", { name: "Pricing" }).click();
  await page.getByRole("option", { name: "Contract price" }).click();
  await page.getByLabel(/Contract amount/).fill(amount);
  await page.getByRole("button", { name: "Activate without payment" }).click();

  const activated = page.getByTestId("offline-billing-current").getByTestId("billed-by-invoice");
  // Not getByText(/billing profile/i): the sidebar's "Billing profile" link would match at once.
  const profileIncomplete = page.getByTestId("billing-profile-incomplete");
  await expect(activated.or(profileIncomplete)).toBeVisible({ timeout: 30_000 });
  return activated.isVisible();
}

export async function verifyBilledByInvoice(page: Page): Promise<void> {
  await expect(
    page.getByTestId("offline-billing-current").getByTestId("billed-by-invoice"),
  ).toHaveText("Billed by invoice");
}

/** Schedules a new contract price; the current period keeps what it was invoiced at. */
export async function repriceFromNextRenewal(page: Page, amount: string): Promise<void> {
  const form = page.getByTestId("offline-repricing-form");
  await form.getByRole("combobox", { name: "Pricing" }).click();
  await page.getByRole("option", { name: "Contract price" }).click();
  await form.getByLabel(/Contract amount/).fill(amount);
  await form.getByRole("button", { name: "Schedule from next renewal" }).click();
  await expect(page.getByTestId("offline-billing-current")).toContainText("from the next renewal", {
    timeout: 20_000,
  });
}

/** The same organization on another subscription page, carried by the URL's organizationId. */
async function openForSameOrganization(page: Page, from: RegExp, to: string): Promise<void> {
  await page.goto(page.url().replace(from, to));
}

/** AC-22: the customer's subscription shows the badge and offers no card prompt. */
export async function verifyCustomerViewHidesCardPrompts(page: Page): Promise<void> {
  await openForSameOrganization(page, /\/subscription\/offline/, "/subscription/simulation");
  await expect(page.getByRole("heading", { name: "Subscription simulation" })).toBeVisible();
  await expect(page.getByTestId("billed-by-invoice")).toBeVisible({ timeout: 20_000 });
  await expect(page.getByTestId("offline-pricing")).toBeVisible();
  for (const prompt of [/Add payment method/, /Change card/, /continue subscription/i, /Complete card setup/]) {
    await expect(page.getByRole("button", { name: prompt })).toHaveCount(0);
    await expect(page.getByRole("link", { name: prompt })).toHaveCount(0);
  }
}

/** Renews now without the gateway, then finds the record document the renewal issued. */
export async function advanceRenewalAndFindRecordDocument(page: Page): Promise<void> {
  await page.getByRole("button", { name: "Advance renewal" }).first().click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();
  await dialog.getByRole("button", { name: "Advance renewal" }).click();
  await expect(page.getByText("Renewal advanced").first()).toBeVisible({ timeout: 30_000 });

  await openForSameOrganization(page, /\/subscription\/simulation/, "/subscription/invoices");
  await expect(page.getByRole("heading", { name: "Invoices & credit notes" })).toBeVisible();
  // Issued like a paid invoice, never called one: a contract invoice is payable by bank transfer,
  // and a no-price subscription's statement is priced under contract.
  await expect(
    page.getByText(/Payable by bank transfer|Priced under contract/).first(),
  ).toBeVisible({ timeout: 60_000 });
}
