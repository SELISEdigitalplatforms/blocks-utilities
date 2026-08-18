import { expect, type Page } from "@playwright/test"
import { openPaymentsSubPage, sidebarNavItem } from "./auth-helpers"
import { readUtilitiesProject } from "./utilities-project"

export async function openUtilitiesConsole(page: Page) {
  await page.goto("/app/console")
  await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
    timeout: 30_000,
  })
}

export async function openUtilitiesDashboard(page: Page) {
  const fixture = readUtilitiesProject()
  if (!fixture?.dashboardUrl) {
    throw new Error("Shared utilities project missing. Run utilities.setup first.")
  }

  await page.goto(fixture.dashboardUrl)
  await expect(page.getByText(/^workspace$/i)).toBeVisible({ timeout: 50_000 })
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

export async function openUtilitiesMagicUrl(page: Page) {
  await openUtilitiesDashboard(page)
  await sidebarNavItem(page, "Magic URL").click()
  await expect(page.getByRole("heading", { name: "Magic URL" })).toBeVisible({
    timeout: 30_000,
  })
}
