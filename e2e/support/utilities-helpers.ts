import { expect, type Page } from "@playwright/test"
import { openPaymentsSubPage, openSubscriptionSubPage, sidebarNavItem } from "./auth-helpers"
import { openNamedProjectDashboard } from "./create-and-delete-project"
import { readUtilitiesProject } from "./utilities-project"

export async function openUtilitiesConsole(page: Page) {
  await page.goto("/app/console")
  await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
    timeout: 30_000,
  })
}

/**
 * Open the shared project dashboard.
 *
 * Direct /app/{id}/dashboard navigation sometimes soft-redirects to the
 * console (seen between Magic URL specs). Fall back to opening the project
 * card the same way setup does.
 */
export async function openUtilitiesDashboard(page: Page) {
  const fixture = readUtilitiesProject()
  if (!fixture?.dashboardUrl || !fixture.projectName) {
    throw new Error("Shared utilities project missing. Run utilities.setup first.")
  }

  const workspace = page.getByText(/^workspace$/i)
  const consoleHeading = page.getByRole("heading", {
    name: /Your Blocks Projects|Welcome to SELISE Blocks/,
  })

  await page.goto(fixture.dashboardUrl, { waitUntil: "domcontentloaded" })

  await Promise.race([
    workspace.waitFor({ state: "visible", timeout: 20_000 }),
    consoleHeading.waitFor({ state: "visible", timeout: 20_000 }),
  ]).catch(() => {})

  if (await workspace.isVisible().catch(() => false)) {
    return
  }

  // Already on console (or dashboard deep-link bounced) — open via project card.
  await openNamedProjectDashboard(page, fixture.projectName)
  await expect(workspace).toBeVisible({ timeout: 30_000 })
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
