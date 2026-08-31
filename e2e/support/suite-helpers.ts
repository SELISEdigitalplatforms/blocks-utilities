import fs from "fs"
import path from "path"
import { type Page } from "@playwright/test"
import {
  openNamedProjectDashboard,
  waitForUtilitiesDashboardReady,
} from "./create-and-delete-project"
import { e2eBaseUrl } from "./env"
import { ensureAuthenticated, isLoginSurface } from "./login-helper"
import {
  UTILITIES_SESSION_PATH,
  readUtilitiesProject,
} from "./utilities-project"

async function persistSuiteSession(page: Page) {
  fs.mkdirSync(path.dirname(UTILITIES_SESSION_PATH), { recursive: true })
  await page.context().storageState({ path: UTILITIES_SESSION_PATH })
}

function sharedDashboardUrl(itemId: string): string {
  return `${e2eBaseUrl()}/app/${itemId}/dashboard`
}

/**
 * Re-seed project/environment localStorage (one Development-chip open), then
 * persist session so later tests can deep-link again.
 */
async function reseedProjectContext(
  page: Page,
  projectName: string,
  dashboardUrl: string | undefined,
) {
  await openNamedProjectDashboard(page, projectName, { dashboardUrl })
  await persistSuiteSession(page)
}

/**
 * Open the shared suite project dashboard via direct URL.
 *
 * Happy path: `goto(/app/{itemId}/dashboard)` using session localStorage from
 * suite setup (must be saved AFTER the project was opened once).
 *
 * Recovery only (login expiry or console bounce): one env-chip open to reseed
 * localStorage, persist session, done — not used on every test.
 */
export async function openSharedProjectDashboard(page: Page) {
  const fixture = readUtilitiesProject()
  if (!fixture?.itemId) {
    throw new Error(
      "Missing fixtures/utilities-project.json (or itemId) — run the utilities-setup project first " +
        "(suite.setup.spec.ts).",
    )
  }

  const targetUrl = sharedDashboardUrl(fixture.itemId)
  const fixtureDashboardUrl = fixture.dashboardUrl || targetUrl

  const gotoDashboard = async () => {
    await page.goto(targetUrl, { waitUntil: "domcontentloaded" })
  }

  await gotoDashboard()

  if (await isLoginSurface(page)) {
    await ensureAuthenticated(page)
    // Re-auth lands on console without project localStorage — reseed once.
    await reseedProjectContext(page, fixture.projectName, fixtureDashboardUrl)
    return
  }

  try {
    await waitForUtilitiesDashboardReady(page, fixture.projectName)
    await persistSuiteSession(page)
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    const bouncedToConsole = /landed on the console/i.test(message)

    let pathname = ""
    try {
      pathname = new URL(page.url()).pathname
    } catch {
      pathname = ""
    }

    if (!bouncedToConsole && !/\/app\/console\/?$/i.test(pathname)) {
      throw error
    }

    // Auth OK but project/env context missing (stale session file) — reseed once.
    await reseedProjectContext(page, fixture.projectName, fixtureDashboardUrl)
  }
}
