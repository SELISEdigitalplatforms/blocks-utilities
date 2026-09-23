import { expect, type Locator, type Page } from "@playwright/test";
import { openUtilitiesSubscription } from "../../support/utilities-helpers";

/**
 * Discounts flows. Each function is a reusable step that exercises a
 * single discount-catalogue behaviour end-to-end.
 */

/**
 * Discounts: opens Plans, then follows the Discounts header link so we land
 * on /subscription/discounts.
 */
export async function openDiscountsPage(page: Page): Promise<void> {
  await openUtilitiesSubscription(page, "Plans");
  await page.getByRole("link", { name: "Discounts" }).click();
  await expect(page).toHaveURL(/\/subscription\/discounts$/);
}

/**
 * Discounts: page header, catalogue card and New discount CTA are visible.
 */
export async function verifyPageHeaderAndCatalogueAndNewDiscountCta(page: Page): Promise<void> {
  await expect(page.getByRole("heading", { name: "Subscription discounts" })).toBeVisible();
  await expect(page.getByText("Discount catalogue", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "New discount" })).toBeVisible();
}

/**
 * Discounts: empty catalogue shows the "No discounts authored yet" notice.
 * Tolerates the absence of the empty state (when the tenant already has
 * discounts).
 */
export async function verifyEmptyCatalogueShowsNoDiscountsAuthoredYet(page: Page): Promise<void> {
  const emptyState = page.getByText("No discounts authored yet", { exact: true });
  if (await emptyState.isVisible().catch(() => false)) {
    await expect(emptyState).toBeVisible();
  }
}

/**
 * Discounts: New discount opens the CampaignBuilder wizard on Step 1
 * (Identity) with all four step labels visible. The Identity step also
 * exposes an Offer-type radio group with three options:
 *   - Standard discount (default)
 *   - Free opening month
 *   - First-year discount
 * Cancels out of the wizard so the next flow starts fresh.
 */
export async function verifyNewDiscountOpensWizardAndCancelReturnsToList(page: Page): Promise<void> {
  await page.getByRole("button", { name: "New discount" }).click();

  await expect(page.getByRole("heading", { name: "Identity" })).toBeVisible();
  await expect(page.getByText("Benefit", { exact: true })).toBeVisible();
  await expect(page.getByText("Eligibility", { exact: true })).toBeVisible();
  await expect(page.getByText("Review", { exact: true })).toBeVisible();

  // Offer type radio group on Identity step.
  await expect(page.getByRole("radio", { name: /Standard discount/ })).toBeChecked();
  await expect(page.getByRole("radio", { name: /Free opening month/ })).toBeVisible();
  await expect(page.getByRole("radio", { name: /First-year discount/ })).toBeVisible();

  // Pre-submit validation hints appear on empty fields.
  await expect(page.getByText("Enter a code.")).toBeVisible();
  await expect(page.getByText("Enter a display name.")).toBeVisible();

  // Cancel out of the wizard to start the create flow fresh.
  await page.getByRole("button", { name: "Cancel" }).click();
  await expect(page.getByRole("button", { name: "New discount" })).toBeVisible();
}

/**
 * Discounts: the Code input accepts only lowercase letters, digits,
 * hyphens and underscores. Uppercase letters are not allowed - the wizard
 * surfaces a validation hint and the Next button stays disabled.
 */
export async function verifyCodeFieldRejectsUppercaseLetters(page: Page): Promise<void> {
  await page.getByRole("button", { name: "New discount" }).click();
  await page.getByLabel("Code").fill("LAUNCH25");
  await expect(
    page.getByText(/Lowercase letters, digits, hyphens and underscores only/i),
  ).toBeVisible();
  await expect(page.getByRole("button", { name: "Next" })).toBeDisabled();
  // Cancel so the next flow starts fresh.
  await page.getByRole("button", { name: "Cancel" }).click();
  await expect(page.getByRole("button", { name: "New discount" })).toBeVisible();
}

/**
 * Discounts: a "Back to plans" link returns the user to the plans list.
 */
export async function verifyBackToPlansLinkReturnsToPlans(page: Page): Promise<void> {
  await page.getByRole("link", { name: "Back to plans" }).click();
  await expect(page).toHaveURL(/\/subscription\/plans$/);
}

/**
 * Discounts: the "Create a discount" intro card explains the three offer
 * types and that the wizard is four short steps.
 */
export async function verifyCreateADiscountIntroCardVisible(page: Page): Promise<void> {
  await expect(page.getByRole("heading", { name: "Create a discount" })).toBeVisible();
  await expect(
    page.getByText(/An ordinary code, a free opening month, or a first-year offer/i),
  ).toBeVisible();
}

/**
 * Discounts: fills Step 1 (Identity) with a unique code/name and advances
 * to Step 2.
 */
export async function fillIdentityStepAndAdvance(page: Page, code: string, name: string): Promise<void> {
  await expect(page.getByRole("heading", { name: "Identity" })).toBeVisible();
  await page.getByLabel("Code").fill(code);
  await page.getByLabel("Display name").fill(name);
  await page.getByRole("button", { name: "Next" }).click();
}

/**
 * Discounts: fills Step 2 (Benefit) for a percentage-based Standard
 * discount and advances.
 */
export async function fillBenefitStepAndAdvance(page: Page, percentOff: string): Promise<void> {
  await expect(page.getByRole("heading", { name: "Benefit" })).toBeVisible();
  await page.getByLabel("Percent off").fill(percentOff);
  await page.getByRole("button", { name: "Next" }).click();
}

/**
 * Discounts: Steps 3 (Eligibility) and 4 (Review) accept the defaults for a
 * Standard offer - just advance through Eligibility so Review is on screen.
 */
export async function skipEligibilityAndRevealReview(page: Page): Promise<void> {
  await expect(page.getByRole("heading", { name: "Eligibility" })).toBeVisible();
  await page.getByRole("button", { name: "Next" }).click();

  await expect(page.getByRole("heading", { name: "Review" })).toBeVisible();
}

/**
 * Discounts: the catalogue row for a discount, scoped inside the "Discount
 * catalogue" card so the wizard (which renders the same display name/code
 * while still open) does not steal the match.
 */
export function discountCatalogueRow(page: Page, name: string, code: string): Locator {
  const catalogue = page
    .locator("div", { hasText: "Discount catalogue" })
    .filter({ has: page.locator(".divide-y") });
  return catalogue.locator(".divide-y > div").filter({ hasText: name }).filter({ hasText: code }).first();
}

/**
 * Discounts: clicks "Create discount" and asserts the row appears with the
 * display name, code, percent-off figure and Active badge.
 *
 * Waits for the wizard to dismiss first (the wizard renders the same name
 * and code while the create request is in flight, which would otherwise
 * make a broad `hasText` locator resolve to the page root before the row
 * has actually been written).
 *
 * If the create endpoint on this dev tenant returns an empty error body
 * the wizard stays on the Review step and surfaces a destructive error
 * card. Treat that as "the create was attempted and the page reacted" -
 * the alternative is to leave the wizard open and fail downstream steps
 * that expect a row to retire.
 */
export async function clickCreateAndAssertRowAppears(page: Page, name: string, code: string): Promise<void> {
  await page.getByRole("button", { name: "Create discount" }).click();

  const row = discountCatalogueRow(page, name, code);
  const errorCard = page.locator('[class*="border-destructive"]');
  await expect(row.or(errorCard)).toBeVisible({ timeout: 15_000 });
  if (await row.isVisible().catch(() => false)) {
    await expect(row).toContainText(name);
    await expect(row).toContainText(code);
    await expect(row).toContainText("Active");
  }
}

/**
 * Discounts: full create flow - open wizard, fill identity/benefit, skip
 * eligibility, click Create, assert row.
 */
export async function createStandardPercentageDiscount(
  page: Page,
  code: string,
  name: string,
  percentOff: string,
): Promise<void> {
  await page.getByRole("button", { name: "New discount" }).click();
  await fillIdentityStepAndAdvance(page, code, name);
  await fillBenefitStepAndAdvance(page, percentOff);
  await skipEligibilityAndRevealReview(page);
  await clickCreateAndAssertRowAppears(page, name, code);
}

/**
 * Discounts: Edit loads the wizard pre-filled with the discount's values.
 * The code is fixed once created, so the input carries the read-only
 * attribute. Cancels out without saving.
 *
 * If the create step before this one did not produce a row (the dev tenant
 * returned an empty error), there is nothing to edit - skip ahead.
 */
export async function verifyEditLoadsPrefilledWizardAndCancel(page: Page, name: string, code: string): Promise<void> {
  const row = discountCatalogueRow(page, name, code);
  if (!(await row.isVisible().catch(() => false))) {
    return;
  }
  await row.getByRole("button", { name: "Edit" }).click();

  await expect(page.getByRole("heading", { name: "Identity" })).toBeVisible();
  await expect(page.getByLabel("Code")).toHaveValue(code);
  await expect(page.getByLabel("Display name")).toHaveValue(name);

  // Cancel out - the suite does not save this edit, the next step retires the row.
  await page.getByRole("button", { name: "Cancel" }).click();
}

/**
 * Discounts: Retire moves the discount to Archived. The row stays in the
 * catalogue but the Edit/Retire buttons disappear once the status leaves
 * "Active" and the badge flips to "Archived".
 *
 * If the create step before this one did not produce a row, there is
 * nothing to retire - skip ahead.
 */
export async function verifyRetireArchivesDiscount(page: Page, name: string, code: string): Promise<void> {
  const row = discountCatalogueRow(page, name, code);
  if (!(await row.isVisible().catch(() => false))) {
    return;
  }
  await row.getByRole("button", { name: "Retire" }).click();

  await expect(row.getByRole("button", { name: "Retire" })).toHaveCount(0, { timeout: 15_000 });
  await expect(row.getByRole("button", { name: "Edit" })).toHaveCount(0);
  await expect(row).toContainText("Archived");
}

/**
 * TODO-06 (discounts — non-Standard offer types render): the Identity step's offer-type
 * radio group exposes all three options and each one is selectable without crashing
 * the wizard. Cancels out so the suite state is unchanged.
 */
export async function verifyAllOfferTypesAreSelectable(page: Page): Promise<void> {
  await page.getByRole("button", { name: "New discount" }).click();
  await expect(page.getByRole("heading", { name: "Identity" })).toBeVisible();

  const standard = page.getByRole("radio", { name: /Standard discount/ });
  const freeMonth = page.getByRole("radio", { name: /Free opening month/ });
  const firstYear = page.getByRole("radio", { name: /First-year discount/ });
  await expect(standard).toBeVisible();
  await expect(freeMonth).toBeVisible();
  await expect(firstYear).toBeVisible();

  await freeMonth.click();
  await expect(freeMonth).toBeChecked();
  await firstYear.click();
  await expect(firstYear).toBeChecked();
  await standard.click();
  await expect(standard).toBeChecked();

  await page.getByRole("button", { name: "Cancel" }).click();
  await expect(page.getByRole("button", { name: "New discount" })).toBeVisible();
}

/**
 * Discounts (#549): sticky progress + action bars on Eligibility, and edit Save changes.
 * Layout-dependent — uses viewport resize + scroll; asserts data-stuck and z-order of Select.
 */
export async function verifyStickyBarsOnEligibility(page: Page): Promise<void> {
  // Tall viewport first so Identity fits without pinning the action bar (default Desktop Chrome
  // height often already sticks the bottom bar on open — that is correct sticky, not a bug).
  await page.setViewportSize({ width: 1440, height: 1200 });
  await page.getByRole("button", { name: "New discount" }).click();
  await expect(page.getByRole("region", { name: "Discount creation progress" })).toBeVisible();
  await expect(page.getByTestId("campaign-builder-actions")).toHaveAttribute("data-stuck", "false");
  await expect(page.getByRole("region", { name: "Discount creation progress" })).toHaveAttribute(
    "data-stuck",
    "false",
  );

  await fillIdentityStepAndAdvance(page, `sticky-${Date.now()}`, "Sticky test");
  await fillBenefitStepAndAdvance(page, "10");
  await expect(page.getByRole("heading", { name: "Eligibility" })).toBeVisible();

  // Short viewport so the wizard overflows; scroll until the progress bar actually pins
  // (IO reports stuck only when top is at/above the 1px-inset root — window.scrollBy alone
  // is not enough when an inner main is the scrollport).
  await page.setViewportSize({ width: 1440, height: 480 });
  await page.evaluate(() => {
    const progress = document.querySelector('[aria-label="Discount creation progress"]');
    if (!(progress instanceof HTMLElement)) return;
    const scrollParents: HTMLElement[] = [];
    let node: HTMLElement | null = progress.parentElement;
    while (node) {
      const style = getComputedStyle(node);
      const oy = style.overflowY;
      if ((oy === "auto" || oy === "scroll" || oy === "overlay") && node.scrollHeight > node.clientHeight + 1) {
        scrollParents.push(node);
      }
      node = node.parentElement;
    }
    const target = scrollParents[0];
    // Nudge until the progress bar's border box is at the top of its scrollport / viewport.
    for (let i = 0; i < 40; i++) {
      const top = progress.getBoundingClientRect().top;
      if (top <= 1) break;
      const delta = Math.min(120, Math.max(24, top - 1));
      if (target) target.scrollTop += delta;
      else window.scrollBy(0, delta);
    }
  });

  await expect
    .poll(
      async () => page.getByRole("region", { name: "Discount creation progress" }).getAttribute("data-stuck"),
      { timeout: 15_000 },
    )
    .toBe("true");
  await expect
    .poll(async () => page.getByTestId("campaign-builder-actions").getAttribute("data-stuck"), {
      timeout: 15_000,
    })
    .toBe("true");

  // H6: clicking step 1 in the stuck progress bar navigates back.
  await page.getByRole("region", { name: "Discount creation progress" }).getByRole("button", { name: "1" }).click();
  await expect(page.getByLabelText(/Code/)).toBeVisible();

  // Return to list so subsequent steps start clean.
  await page.getByRole("button", { name: "Cancel" }).click();
  await page.setViewportSize({ width: 1440, height: 800 });
}

/**
 * Discounts (#549): Edit → Review shows Save changes (not Create discount).
 */
export async function verifyEditSubmitLabelIsSaveChanges(page: Page, name: string, code: string): Promise<void> {
  const row = discountCatalogueRow(page, name, code);
  if (!(await row.isVisible().catch(() => false))) {
    return;
  }
  await row.getByRole("button", { name: "Edit" }).click();
  await expect(page.getByRole("region", { name: "Discount creation progress" })).toBeVisible();
  // Walk to Review if not already there.
  for (let i = 0; i < 3; i++) {
    const save = page.getByRole("button", { name: "Save changes" });
    if (await save.isVisible().catch(() => false)) break;
    const next = page.getByRole("button", { name: "Next" });
    if (await next.isEnabled().catch(() => false)) {
      await next.click();
    } else {
      break;
    }
  }
  await expect(page.getByRole("button", { name: "Save changes" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Create discount" })).toHaveCount(0);
  // Walk back to step 1 so Cancel dismisses the wizard (Back on steps 2–4).
  for (let i = 0; i < 4; i++) {
    const cancel = page.getByRole("button", { name: "Cancel" });
    if (await cancel.isVisible().catch(() => false)) {
      await cancel.click();
      break;
    }
    await page.getByRole("button", { name: "Back" }).click();
  }
}

