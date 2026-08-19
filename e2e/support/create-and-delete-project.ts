import { Page, expect, test } from "@playwright/test"
import { ensureAuthenticatedOnCurrentOrigin } from "./login-helper"

const ORPHAN_PROJECT_PATTERN = /Test Project \d+/g
const OS_APP_NAME = /^(?:Blocks\s+)?OS(?:\s+OS)?$/i
const HOME_APP_NAME = /Utilities/i
const HOME_APP_URL = /dev-utilities|localhost/

async function listOrphanProjectNames(page: Page): Promise<string[]> {
  const bodyText = await page.locator("body").innerText().catch(() => "")
  return [...new Set([...bodyText.matchAll(ORPHAN_PROJECT_PATTERN)].map((match) => match[0]))]
}

const isVisibleNow = async (locator: { isVisible: (opts: { timeout: number }) => Promise<boolean> }) =>
  locator.isVisible({ timeout: 500 }).catch(() => false)

const isUtilitiesApp = (page: Page) => /dev-utilities|localhost/i.test(page.url())

export async function ensureConsole(page: Page) {
  const href = page.url()
  const isHttp = /^https?:/.test(href)

  if (isHttp) {
    const url = new URL(href)
    if (/\/app\/console\/?$/.test(url.pathname)) {
      await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
        timeout: 30_000,
      })
      return
    }
    await page.goto(`${url.origin}/app/console`)
  } else {
    await page.goto("/app/console")
  }

  await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
    timeout: 30_000,
  })
}

export function namedProjectCard(page: Page, projectName: string) {
  return page
    .locator("div")
    .filter({ has: page.getByText(projectName, { exact: true }) })
    .filter({
      has: page.getByRole("button", {
        name: /Development|Testing|Staging|IAT|UAT|Production|Pre-Prod|Prod Shadow/,
      }),
    })
    .last()
}

async function waitForProjectCard(page: Page, projectName: string) {
  for (let attempt = 0; attempt < 12; attempt++) {
    await ensureConsole(page)

    const card = namedProjectCard(page, projectName)
    if (await card.isVisible({ timeout: 2_000 }).catch(() => false)) {
      return card
    }

    await page.reload({ waitUntil: "domcontentloaded" })
    await page.waitForTimeout(1_500)
  }

  throw new Error(`Project "${projectName}" did not appear on the console`)
}

async function waitForDashboardReady(page: Page, projectName: string) {
  await expect(page).toHaveURL(/\/app\/(?!project\/)[^/]+\/dashboard/, { timeout: 30_000 })

  if (isUtilitiesApp(page)) {
    await expect(page.getByText(/^workspace$/i)).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText(projectName, { exact: true }).first()).toBeVisible({ timeout: 30_000 })
    return
  }

  await expect(
    page
      .getByRole("button", { name: "Delete", exact: true })
      .or(page.getByText(/X-Blocks-Key/))
      .first(),
  ).toBeVisible({ timeout: 30_000 })
}

export async function openNamedProjectDashboard(page: Page, projectName: string) {
  for (let attempt = 0; attempt < 4; attempt++) {
    const card = await waitForProjectCard(page, projectName)
    const development = card.getByRole("button", { name: /Development/ }).first()
    await expect(development).toBeVisible({ timeout: 15_000 })
    await development.click({ force: true })

    try {
      await waitForDashboardReady(page, projectName)
      return
    } catch (error) {
      if (attempt === 3) throw error
    }
  }
}

async function clickAppSwitcherAndNavigate(page: Page, appNamePattern: RegExp) {
  const appSwitcher = page.getByRole("button", { name: "SELISE Blocks apps" })
  await expect(appSwitcher).toBeVisible({ timeout: 10_000 })
  await appSwitcher.click()

  const appLink = page
    .getByRole("link", { name: appNamePattern })
    .or(page.getByRole("button", { name: appNamePattern }))
    .or(page.getByText(appNamePattern))
    .first()
  await expect(appLink).toBeVisible({ timeout: 5_000 })
  await appLink.click()
}

async function freeProjectSlotIfNeeded(page: Page) {
  await ensureConsole(page)

  const welcomeHeading = page.getByRole("heading", { name: "Welcome to SELISE Blocks" })
  if (await isVisibleNow(welcomeHeading)) return

  const addProjectButton = page.getByText("Add Project", { exact: true }).first()
  if (await isVisibleNow(addProjectButton)) return

  for (let attempt = 0; attempt < 8; attempt++) {
    const orphanNames = await listOrphanProjectNames(page)
    if (orphanNames.length === 0) break

    await deleteCreatedProject(page, orphanNames[0])
    await ensureConsole(page)

    if (await isVisibleNow(addProjectButton)) return
  }

  await expect(addProjectButton).toBeVisible({ timeout: 15_000 })
}

/**
 * Creates a project via the OS-hosted wizard (Utilities redirects to OS for this).
 *
 * Flow:
 *   Utilities console → "Add Project" → OS create-project wizard →
 *   project created on OS environments page → app switcher → Utilities →
 *   Utilities console → click the new project → Utilities dashboard
 */
export async function createProject(page: Page) {
  await test.step("Start a new project (redirects to OS)", async () => {
    const welcomeHeading = page.getByRole("heading", { name: "Welcome to SELISE Blocks" })
    const createProjectButton = page.getByRole("button", { name: "Create a project" })
    const addProjectButton = page.getByText("Add Project", { exact: true }).first()
    const consoleHeading = page.getByRole("heading", { name: "Your Blocks Projects" })

    await Promise.race([
      welcomeHeading.waitFor({ state: "visible", timeout: 50_000 }),
      addProjectButton.waitFor({ state: "visible", timeout: 50_000 }),
      consoleHeading.waitFor({ state: "visible", timeout: 50_000 }),
    ])

    await freeProjectSlotIfNeeded(page)

    if (await welcomeHeading.isVisible().catch(() => false)) {
      await createProjectButton.click()
    } else {
      await expect(addProjectButton).toBeVisible({ timeout: 15_000 })
      await addProjectButton.click()
    }
    await page.waitForURL((url) => url.pathname.endsWith("/app/create-project"), {
      timeout: 45_000,
    })
  })

  const projectName = `Test Project ${Date.now()}`
  await test.step("Name the project and accept the agreements", async () => {
    await expect(page.getByRole("heading", { name: "Name your project" })).toBeVisible({
      timeout: 30_000,
    })
    const nameInput = page.locator('[placeholder="Enter your project name"]:visible')
    await nameInput.fill(projectName)

    await page.getByRole("checkbox", { name: "I confirm that I will use" }).click()
    await page.getByRole("checkbox", { name: "I accept the Terms of services" }).click()

    const continueButton = page.getByRole("button", { name: "Continue", exact: true })
    await expect(continueButton).toBeEnabled()
    await continueButton.click()
  })

  await test.step("Skip optional repositories", async () => {
    await expect(page.getByRole("heading", { name: "Add resource" })).toBeVisible({
      timeout: 30_000,
    })
    await page.getByRole("button", { name: "Continue", exact: true }).click()
  })

  await test.step("Select Development and submit", async () => {
    await expect(
      page.getByText("Select environments", { exact: true }).and(page.locator(":visible")),
    ).toBeVisible({ timeout: 30_000 })

    await page.getByText("Development", { exact: true }).and(page.locator(":visible")).click()
    const submitButton = page.getByRole("button", { name: "Submit" })
    await expect(submitButton).toBeEnabled()
    await submitButton.click()
  })

  await test.step("Wait for create success (on OS)", async () => {
    await expect(page.getByText("Your project has been created.", { exact: true })).toBeVisible({
      timeout: 30_000,
    })
    await expect(page).toHaveURL(/\/app\/project\/[^/]+\/environments$/, {
      timeout: 20_000,
    })
  })

  await test.step("Switch back to Utilities via app switcher", async () => {
    await clickAppSwitcherAndNavigate(page, HOME_APP_NAME)

    await page.waitForURL(HOME_APP_URL, { timeout: 30_000 })
    await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
      timeout: 30_000,
    })
  })

  await test.step("Open the newly created project on Utilities", async () => {
    await openNamedProjectDashboard(page, projectName)
  })

  return { projectName }
}

async function deleteProjectOnCurrentApp(page: Page, projectName: string) {
  await ensureConsole(page)
  await openNamedProjectDashboard(page, projectName)

  await expect(
    page.getByRole("button", { name: "Delete", exact: true }),
  ).toBeVisible({ timeout: 30_000 })

  await page.getByRole("button", { name: "Delete", exact: true }).click()

  await expect(
    page.getByRole("heading", { name: "Delete this environment?" }),
  ).toBeVisible()

  await page.getByRole("button", { name: "Delete", exact: true }).last().click()

  await expect(page.getByText("Successfully deleted", { exact: true })).toBeVisible({
    timeout: 20_000,
  })
  await expect(page).toHaveURL(/\/app\/console$/, { timeout: 20_000 })
}

async function deleteOrphanTestProjectsOnOs(page: Page) {
  for (let attempt = 0; attempt < 8; attempt++) {
    await ensureConsole(page)
    const orphanNames = await listOrphanProjectNames(page)
    if (orphanNames.length === 0) return

    await deleteProjectOnCurrentApp(page, orphanNames[0])
  }

  await ensureConsole(page)
  const leftover = await listOrphanProjectNames(page)
  if (leftover.length > 0) {
    throw new Error(`Failed to delete leftover e2e projects: ${leftover.join(", ")}`)
  }
}

export async function deleteCreatedProject(page: Page, projectName?: string) {
  await test.step("Open the app console before teardown", async () => {
    await ensureConsole(page)
  })

  await test.step("Switch to Blocks OS and delete e2e projects", async () => {
    await clickAppSwitcherAndNavigate(page, OS_APP_NAME)
    await page.waitForURL(/dev-os/, { timeout: 30_000 })
    await ensureAuthenticatedOnCurrentOrigin(page)
    await ensureConsole(page)

    if (projectName) {
      await deleteProjectOnCurrentApp(page, projectName)
      await expect(page.getByText(projectName, { exact: true })).toHaveCount(0, {
        timeout: 20_000,
      })
    }

    await deleteOrphanTestProjectsOnOs(page)
  })

  await test.step("Return to Utilities and verify e2e projects are gone", async () => {
    await clickAppSwitcherAndNavigate(page, HOME_APP_NAME)
    await page.waitForURL(HOME_APP_URL, { timeout: 30_000 })
    await ensureConsole(page)

    if (projectName) {
      await expect(page.getByText(projectName, { exact: true })).toHaveCount(0, {
        timeout: 20_000,
      })
    }
    await expect(page.getByText(/^Test Project \d+$/)).toHaveCount(0, { timeout: 10_000 })
  })
}
