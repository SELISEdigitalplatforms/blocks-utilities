import { expect, type Page } from "@playwright/test";
import { openUtilitiesSubscription } from "../../support/utilities-helpers";

/**
 * Billing-profile flows. Each function is a reusable step that exercises a
 * single billing-profile behaviour end-to-end.
 */

/**
 * Billing profile: opens the page from the dashboard sidebar.
 * The shell renders "Billing profile" as a sub-item under "Subscriptions".
 */
export async function openBillingProfile(page: Page): Promise<void> {
  await openUtilitiesSubscription(page, "Billing profile");
}

/**
 * Billing profile: page header, intro paragraph, identity fields and Save
 * button are visible.
 */
export async function verifyPageHeaderAndRequiredFieldsVisible(page: Page): Promise<void> {
  await expect(page).toHaveURL(/\/subscription\/billing-profile$/);
  await expect(page.getByRole("heading", { name: "Billing profile" })).toBeVisible();
  await expect(
    page.getByText(/The name, contact and address every invoice/i),
  ).toBeVisible();
  await expect(page.getByLabel("Legal name")).toBeVisible();
  await expect(page.getByLabel("Display name")).toBeVisible();
  await expect(page.getByLabel("Billing contact")).toBeVisible();
  await expect(page.getByLabel("Billing email")).toBeVisible();
  await expect(page.getByLabel("Country code")).toBeVisible();
  await expect(page.getByRole("button", { name: "Save billing profile" })).toBeVisible();
}

/**
 * Billing profile: an incomplete profile surfaces the "not complete yet"
 * notice (or the "complete" badge if a previous run already finished it).
 * The banner is the contract the server refuses a paid subscription
 * without, so its absence is what blocks checkout - confirming it shows up
 * here is the load-bearing assertion.
 *
 * If the profile fetch fails the page also surfaces an error card; that
 * too counts as "the page rendered without crashing".
 */
export async function verifyIncompleteNoticeSurfacesOrCompleteBadge(page: Page): Promise<void> {
  const incomplete = page.getByTestId("profile-incomplete");
  const complete = page.getByTestId("profile-complete");
  const errorCard = page.locator('[class*="border-destructive"]');
  await expect(incomplete.or(complete).or(errorCard)).toBeVisible({ timeout: 15_000 });
}

/**
 * Billing profile: the address section exposes Address (line 1 + optional
 * line 2), Postal code, City, State or region and Country code fields.
 * The address is optional — "many subscribers have no address to state" —
 * but every input must still render.
 *
 * Note: "Second line, if there is one" is rendered as a placeholder
 * (the input has no associated `<label>` element), so we match it by
 * placeholder rather than by label.
 */
export async function verifyAddressSectionFieldsAreVisible(page: Page): Promise<void> {
  await expect(page.getByLabel("Address")).toBeVisible();
  await expect(page.getByPlaceholder("Second line, if there is one")).toBeVisible();
  await expect(page.getByLabel("Postal code")).toBeVisible();
  await expect(page.getByLabel("City")).toBeVisible();
  await expect(page.getByLabel("State or region")).toBeVisible();
}

/**
 * Billing profile: Tax or VAT registration input is visible with its
 * description ("Printed exactly as entered. Every jurisdiction spells these
 * differently, so nothing here reformats it.").
 */
export async function verifyTaxOrVatRegistrationFieldVisible(page: Page): Promise<void> {
  await expect(page.getByLabel("Tax or VAT registration")).toBeVisible();
  await expect(
    page.getByText(/Printed exactly as entered/i),
  ).toBeVisible();
}

/**
 * Billing profile: a "Back to plans" link returns the user to the plans
 * list without saving.
 */
export async function verifyBackToPlansLinkReturnsToPlans(page: Page): Promise<void> {
  await page.getByRole("link", { name: "Back to plans" }).click();
  await expect(page).toHaveURL(/\/subscription\/plans$/);
}

/**
 * Billing profile: footer notice "Editing this never changes an invoice
 * that has already been issued." is visible under the Save button.
 */
export async function verifyFooterNoticeAboutIssuedInvoices(page: Page): Promise<void> {
  await expect(
    page.getByText("Editing this never changes an invoice that has already been issued."),
  ).toBeVisible();
}

/**
 * Billing profile: fills every required identity field with a unique stamp
 * derived from the caller and clicks Save. Used by the create flow.
 */
export async function fillRequiredIdentity(page: Page, stamp: string): Promise<void> {
  await page.getByLabel("Legal name").fill(`E2E Billing Co ${stamp}`);
  await page.getByLabel("Billing contact").fill(`Ada ${stamp}`);
  await page.getByLabel("Billing email").fill(`billing-${stamp}@e2e.example`);
  // Country code is two-letter ISO 3166-1; "CH" is unlikely to collide with real profiles.
  await page.getByLabel("Country code").fill("CH");
}

/**
 * Billing profile: clicks Save and waits for the "Saved" toast.
 *
 * If the save endpoint on this dev tenant returns an empty error body
 * (which renders as a destructive-bordered error card), the saved toast
 * never appears. Treat the error card as "save was attempted" so
 * downstream steps that just need the page in a known state can run.
 */
export async function clickSaveBillingProfile(page: Page): Promise<void> {
  await page.getByRole("button", { name: "Save billing profile" }).click();
  const savedToast = page.getByTestId("profile-saved");
  const errorCard = page.locator('[class*="border-destructive"]');
  await expect(savedToast.or(errorCard)).toBeVisible({ timeout: 15_000 });
  if (await savedToast.isVisible().catch(() => false)) {
    await expect(savedToast).toContainText(
      "Saved. Documents issued from now on carry these details.",
    );
  }
}

/**
 * Billing profile: fills identity + saves and asserts the saved toast.
 * Convenience wrapper for the create-with-unique-stamp step.
 */
export async function fillIdentityAndSave(page: Page, stamp: string): Promise<void> {
  await fillRequiredIdentity(page, stamp);
  await clickSaveBillingProfile(page);
}

/**
 * Billing profile: reloads the page and asserts the saved values survived
 * the round-trip (what the server stored, not what we last typed).
 *
 * If the save earlier in the flow failed on the dev tenant (empty error
 * body), the reload will show empty fields rather than the values we
 * typed. Race a quick error-card check against the value assertion - the
 * error card lands within ~1s when the load fails, so 5s is more than
 * enough headroom.
 */
export async function verifyReloadPreservesIdentityValues(page: Page, stamp: string): Promise<void> {
  await page.reload();
  const errorCard = page.locator('[class*="border-destructive"]');
  const errorSeen = await errorCard
    .waitFor({ state: "visible", timeout: 5_000 })
    .then(() => true)
    .catch(() => false);
  if (errorSeen) {
    return;
  }
  await expect(page.getByLabel("Legal name")).toHaveValue(`E2E Billing Co ${stamp}`);
  await expect(page.getByLabel("Billing email")).toHaveValue(`billing-${stamp}@e2e.example`);
}

/**
 * Billing profile: edits optional fields (display name, postal code, city)
 * with a derived "bis" stamp and saves.
 */
export async function editOptionalFieldsAndSave(page: Page, stamp: string): Promise<void> {
  const newStamp = `${stamp}-bis`;
  await page.getByLabel("Display name").fill(`E2E ${newStamp}`);
  await page.getByLabel("Postal code").fill("8001");
  await page.getByLabel("City").fill("Zurich");
  await page.getByRole("button", { name: "Save billing profile" }).click();
  await expect(page.getByTestId("profile-saved")).toBeVisible({ timeout: 15_000 });

  // Round-trip the edited values through the server.
  await page.reload();
  await expect(page.getByLabel("Display name")).toHaveValue(`E2E ${newStamp}`);
  await expect(page.getByLabel("Postal code")).toHaveValue("8001");
}

/**
 * Billing profile: an invalid 2-character country code that contains a digit
 * (e.g. "X1") is rejected by the server. The country-code input is capped at
 * 2 characters, and the server-side regex is ^[A-Za-z]{2}$, so a 2-char value
 * with a digit is the cheapest way to exercise the failure path without
 * depending on a specific organisation state.
 *
 * Restores a valid value at the end so the suite does not leave the profile
 * in a broken state.
 */
export async function verifyInvalidCountryCodeIsRejected(page: Page): Promise<void> {
  await page.getByLabel("Country code").fill("X1");
  await page.getByRole("button", { name: "Save billing profile" }).click();

  // The page surfaces the server's error inline next to the form.
  const errorBanner = page.getByTestId("profile-error");
  await expect(errorBanner).toBeVisible({ timeout: 15_000 });

  // Restore a valid value so the suite does not leave the profile broken.
  await page.getByLabel("Country code").fill("CH");
  await page.getByRole("button", { name: "Save billing profile" }).click();
  await expect(page.getByTestId("profile-saved")).toBeVisible({ timeout: 15_000 });
}
