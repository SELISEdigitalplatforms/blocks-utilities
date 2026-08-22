import { Page, expect, test } from "@playwright/test"
import { e2eBaseUrl, e2eOsBaseUrl, e2eProjectId } from "./env"
import { ensureAuthenticated, ensureAuthenticatedOnCurrentOrigin } from "./login-helper"

const ORPHAN_PROJECT_PATTERN = /Test Project \d+/g
const ENV_BUTTON =
  /Development|Testing|Staging|IAT|UAT|Production|Pre-Prod|Prod Shadow/

const isVisibleNow = async (locator: { isVisible: (opts: { timeout: number }) => Promise<boolean> }) =>
  locator.isVisible({ timeout: 500 }).catch(() => false)

async function listOrphanProjectNames(page: Page): Promise<string[]> {
  const bodyText = await page.locator("body").innerText().catch(() => "")
  return [...new Set([...bodyText.matchAll(ORPHAN_PROJECT_PATTERN)].map((match) => match[0]))]
}

function consoleBase(host: "utilities" | "os" = "utilities") {
  return host === "os" ? e2eOsBaseUrl() : e2eBaseUrl()
}

/** Blocks console on Utilities or OS. */
export async function ensureConsole(page: Page, host: "utilities" | "os" = "utilities") {
  const base = consoleBase(host)
  const href = page.url()
  const onConsole =
    /^https?:/.test(href) &&
    new URL(href).origin === new URL(base).origin &&
    /\/app\/console\/?$/.test(new URL(href).pathname)

  if (!onConsole) {
    await page.goto(`${base}/app/console`, { waitUntil: "domcontentloaded" })
  }

  await expect(
    page.getByRole("heading", { name: /Your Blocks Projects|Welcome to SELISE Blocks/ }),
  ).toBeVisible({ timeout: 20_000 })
}

export function namedProjectCard(page: Page, projectName: string) {
  return page
    .locator("div")
    .filter({ has: page.getByText(projectName, { exact: true }) })
    .filter({
      has: page.getByRole("button", { name: ENV_BUTTON }),
    })
    .last()
}

async function waitForProjectCard(page: Page, projectName: string, host: "utilities" | "os" = "utilities") {
  for (let attempt = 0; attempt < 6; attempt++) {
    await ensureConsole(page, host)

    const card = namedProjectCard(page, projectName)
    if (await card.isVisible({ timeout: 1_500 }).catch(() => false)) {
      return card
    }

    if (attempt < 5) {
      await page.reload({ waitUntil: "domcontentloaded" })
      await page.waitForTimeout(500)
    }
  }

  throw new Error(`Project "${projectName}" did not appear on the ${host} console`)
}

/** Utilities project dashboard — workspace shell visible. */
async function waitForUtilitiesDashboardReady(page: Page, projectName: string) {
  await expect(page).toHaveURL(/\/app\/(?!project\/)[^/]+\/dashboard/, { timeout: 20_000 })
  await expect(page.getByText(/^workspace$/i)).toBeVisible({ timeout: 20_000 })
  await expect(page.getByText(projectName, { exact: true }).first()).toBeVisible({ timeout: 20_000 })
}

/** OS project dashboard — project name heading + Delete button. */
async function waitForOsDashboardReady(page: Page, projectName: string) {
  await expect(page).toHaveURL(/\/app\/(?!project\/)[^/]+\/dashboard/, { timeout: 20_000 })
  await expect(page.getByRole("heading", { name: projectName })).toBeVisible({ timeout: 20_000 })
  await expect(page.getByRole("button", { name: "Delete", exact: true })).toBeVisible({
    timeout: 20_000,
  })
}

async function readProjectNameFromDashboard(page: Page): Promise<string> {
  const sidebarProject = page.getByRole("button", { name: /^Project / })
  if (await sidebarProject.isVisible({ timeout: 3_000 }).catch(() => false)) {
    const label = await sidebarProject.innerText()
    return label.replace(/^Project\s+/i, "").trim()
  }

  const details = page
    .locator("main")
    .filter({ has: page.getByRole("heading", { name: "Project Details" }) })
  const nameBlock = details.getByText(/^Name\s+\S/, { exact: false }).first()
  if (await nameBlock.isVisible({ timeout: 3_000 }).catch(() => false)) {
    return (await nameBlock.innerText()).replace(/^Name\s+/, "").trim()
  }

  throw new Error(`Could not read project name from dashboard: ${page.url()}`)
}

async function openProjectById(page: Page, projectId: string) {
  await page.goto(`${e2eBaseUrl()}/app/${projectId}/dashboard`, { waitUntil: "domcontentloaded" })
  await expect(page.getByText(/^workspace$/i)).toBeVisible({ timeout: 20_000 })

  const reuseName = process.env.E2E_REUSE_PROJECT_NAME?.trim()
  if (reuseName) {
    await expect(page.getByText(reuseName, { exact: true }).first()).toBeVisible({
      timeout: 20_000,
    })
    return { projectName: reuseName, dashboardUrl: page.url(), itemId: projectId }
  }

  const overview = page
    .getByRole("link", { name: "Overview", exact: true })
    .or(page.getByRole("button", { name: "Overview", exact: true }))
  if (await overview.first().isVisible({ timeout: 5_000 }).catch(() => false)) {
    await overview.first().click()
    await expect(page.getByRole("heading", { name: "Project Details" })).toBeVisible({
      timeout: 20_000,
    })
  }

  const projectName = await readProjectNameFromDashboard(page)
  await page.goto(`${e2eBaseUrl()}/app/${projectId}/dashboard`, { waitUntil: "domcontentloaded" })
  await expect(page.getByText(/^workspace$/i)).toBeVisible({ timeout: 20_000 })
  return { projectName, dashboardUrl: page.url(), itemId: projectId }
}

export async function openNamedProjectDashboard(
  page: Page,
  projectName: string,
  options?: { dashboardUrl?: string },
) {
  if (options?.dashboardUrl) {
    await page.goto(options.dashboardUrl, { waitUntil: "domcontentloaded" })
    try {
      await waitForUtilitiesDashboardReady(page, projectName)
      return
    } catch {
      // Fall through to card navigation.
    }
  }

  for (let attempt = 0; attempt < 3; attempt++) {
    const card = await waitForProjectCard(page, projectName, "utilities")
    const envButton = card.getByRole("button", { name: ENV_BUTTON }).first()
    await expect(envButton).toBeVisible({ timeout: 10_000 })
    await envButton.click({ force: true })

    try {
      await waitForUtilitiesDashboardReady(page, projectName)
      return
    } catch (error) {
      if (attempt === 2) throw error
    }
  }
}

async function openOsProjectDashboard(page: Page, projectName: string) {
  await ensureConsole(page, "os")

  for (let attempt = 0; attempt < 3; attempt++) {
    const card = await waitForProjectCard(page, projectName, "os")
    const envButton = card.getByRole("button", { name: ENV_BUTTON }).first()
    await expect(envButton).toBeVisible({ timeout: 10_000 })
    await envButton.click({ force: true })

    try {
      await waitForOsDashboardReady(page, projectName)
      return
    } catch (error) {
      if (attempt === 2) throw error
    }
  }
}

async function deleteProjectOnOs(page: Page, projectName: string): Promise<boolean> {
  await page.goto(`${e2eOsBaseUrl()}/app/console`, { waitUntil: "domcontentloaded" })
  await ensureAuthenticatedOnCurrentOrigin(page)
  await openOsProjectDashboard(page, projectName)

  await page.getByRole("button", { name: "Delete", exact: true }).click()
  await expect(page.getByRole("heading", { name: "Delete this environment?" })).toBeVisible()
  await page.getByRole("button", { name: "Delete", exact: true }).last().click()
  await expect(page.getByText("Successfully deleted", { exact: true })).toBeVisible({
    timeout: 15_000,
  })
  await expect(page).toHaveURL(/\/app\/console$/, { timeout: 15_000 })
  return true
}

async function clickAppSwitcherAndNavigate(page: Page, appNamePattern: RegExp) {
  const appSwitcher = page.getByRole("button", { name: "SELISE Blocks apps" })
  await expect(appSwitcher).toBeVisible({ timeout: 10_000 })
  await appSwitcher.click()

  const appLink = page.getByText(appNamePattern).first()
  await expect(appLink).toBeVisible({ timeout: 5_000 })
  await appLink.click()
}

async function freeProjectSlotIfNeeded(page: Page) {
  await ensureConsole(page, "utilities")

  const welcomeHeading = page.getByRole("heading", { name: "Welcome to SELISE Blocks" })
  if (await isVisibleNow(welcomeHeading)) return

  const addProjectButton = page.getByText("Add Project", { exact: true }).first()
  if (await isVisibleNow(addProjectButton)) return

  const atProjectLimit = page.getByText("Please delete an existing project to create a new one.")
  if (!(await isVisibleNow(atProjectLimit))) return

  for (let attempt = 0; attempt < 8; attempt++) {
    const orphanNames = await listOrphanProjectNames(page)
    if (orphanNames.length === 0) break

    await deleteCreatedProject(page, orphanNames[0]).catch(() => {})
    await ensureConsole(page, "utilities")

    if (await isVisibleNow(addProjectButton)) return
  }

  await expect(addProjectButton).toBeVisible({ timeout: 15_000 })
}

/**
 * Creates a project via the OS-hosted wizard (Utilities redirects to OS for this).
 *
 * Flow:
 *   Utilities console → "Add Project" → redirected to OS create-project wizard →
 *   fill wizard → project created on OS environments page →
 *   app switcher → click Utilities → Utilities console → click the new project →
 *   Utilities dashboard (workspace shell visible)
 */
export async function createProject(page: Page) {
  await test.step("Start a new project (redirects to OS)", async () => {
    await ensureAuthenticated(page)
    await ensureConsole(page, "utilities")

    const welcomeHeading = page.getByRole("heading", { name: "Welcome to SELISE Blocks" })
    const createProjectButton = page.getByRole("button", { name: "Create a project" })
    const addProjectButton = page.getByText("Add Project", { exact: true }).first()

    await freeProjectSlotIfNeeded(page)

    if (await welcomeHeading.isVisible().catch(() => false)) {
      await createProjectButton.click()
    } else {
      await expect(addProjectButton).toBeVisible({ timeout: 15_000 })
      await addProjectButton.click()
    }
    await expect(page).toHaveURL(/\/app\/create-project$/, { timeout: 15_000 })
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
    await clickAppSwitcherAndNavigate(page, /Utilities/i)
    await page.waitForURL(/dev-utilities|localhost/i, { timeout: 30_000 })
    await ensureConsole(page, "utilities")
  })

  await test.step("Open the newly created project on Utilities", async () => {
    await openNamedProjectDashboard(page, projectName)
  })

  return { projectName, dashboardUrl: page.url() }
}

/** Reuse an existing project, or create one when Add Project is available. */
export async function reuseOrCreateSharedProject(
  page: Page,
): Promise<{ projectName: string; dashboardUrl: string; itemId: string }> {
  await ensureAuthenticated(page)

  const configuredProjectId = e2eProjectId()
  if (configuredProjectId) {
    return openProjectById(page, configuredProjectId)
  }

  await ensureConsole(page, "utilities")

  const reuseName = process.env.E2E_REUSE_PROJECT_NAME?.trim()
  if (reuseName) {
    await openNamedProjectDashboard(page, reuseName)
    const itemId = new URL(page.url()).pathname.split("/")[2] ?? ""
    return { projectName: reuseName, dashboardUrl: page.url(), itemId }
  }

  const testProjects = await listOrphanProjectNames(page)
  if (testProjects.length > 0) {
    const projectName = testProjects[testProjects.length - 1]!
    await openNamedProjectDashboard(page, projectName)
    const itemId = new URL(page.url()).pathname.split("/")[2] ?? ""
    return { projectName, dashboardUrl: page.url(), itemId }
  }

  const addProjectButton = page.getByText("Add Project", { exact: true }).first()
  if (await addProjectButton.isVisible({ timeout: 2_000 }).catch(() => false)) {
    const created = await createProject(page)
    const itemId = new URL(created.dashboardUrl).pathname.split("/")[2] ?? ""
    return { ...created, itemId }
  }

  throw new Error(
    "No project to reuse and Add Project is unavailable. " +
      "Set E2E_REUSE_PROJECT_NAME (e.g. test) or E2E_PROJECT_ID, or free a console slot.",
  )
}

/** Delete project on Blocks OS (only place with project Delete UI). */
export async function deleteCreatedProject(
  page: Page,
  projectName?: string,
  options?: { itemId?: string },
): Promise<boolean> {
  if (!projectName) return false
  void options

  return test.step("Delete project on Blocks OS", async () => {
    try {
      const deleted = await deleteProjectOnOs(page, projectName)
      if (deleted) {
        await ensureConsole(page, "os")
        await expect(page.getByText(projectName, { exact: true })).toHaveCount(0, {
          timeout: 10_000,
        })
      }
      return deleted
    } catch (error) {
      console.warn(`[e2e] Failed to delete project "${projectName}" on OS:`, error)
      return false
    }
  })
}
