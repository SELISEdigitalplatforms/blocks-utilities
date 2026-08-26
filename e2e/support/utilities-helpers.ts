import { expect, type Page } from "@playwright/test"
import { openPaymentsSubPage, openSubscriptionSubPage, sidebarNavItem } from "./auth-helpers"
import { ensureAuthenticated } from "./login-helper"
import { openSharedProjectDashboard } from "./suite-helpers"

/** Open the Utilities console; re-login if the suite session expired. */
export async function openUtilitiesConsole(page: Page) {
  await ensureAuthenticated(page)
  await expect(
    page.getByRole("heading", { name: /Your Blocks Projects|Welcome to SELISE Blocks/ }),
  ).toBeVisible({ timeout: 30_000 })
}

/** Open the shared suite project dashboard (re-login + same project if session expired). */
export async function openUtilitiesDashboard(page: Page) {
  await openSharedProjectDashboard(page)
}

export async function openUtilitiesOverview(page: Page) {
  await openUtilitiesDashboard(page)
  await sidebarNavItem(page, "Overview").click()
  await expect(page.getByRole("heading", { name: "Project Details" })).toBeVisible({
    timeout: 30_000,
  })
}

export async function openUtilitiesPayments(
  page: Page,
  name: "Create Payment" | "Payment List" | "Saved Cards" | "Payment Providers",
) {
  await openUtilitiesDashboard(page)
  await openPaymentsSubPage(page, name)
}

export async function openUtilitiesSubscription(page: Page, name: "Plans" | "Simulation") {
  await openUtilitiesDashboard(page)
  await openSubscriptionSubPage(page, name)
}

export async function openUtilitiesMagicUrl(page: Page) {
  await openUtilitiesDashboard(page)
  await sidebarNavItem(page, "Magic URL").click()
  await expect(page.getByRole("heading", { name: "Magic URL" })).toBeVisible({
    timeout: 30_000,
  })
}
