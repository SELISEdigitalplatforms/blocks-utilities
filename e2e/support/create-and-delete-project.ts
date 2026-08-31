import { Page, expect, test } from "@playwright/test"
import { e2eBaseUrl, e2eOsBaseUrl, e2eProjectId } from "./env"
import { ensureAuthenticated, ensureAuthenticatedOnCurrentOrigin } from "./login-helper"

const ENV_BUTTON =
  /Development|Testing|Staging|IAT|UAT|Production|Pre-Prod|Prod Shadow/

const isVisibleNow = async (locator: { isVisible: (opts: { timeout: number }) => Promise<boolean> }) =>
  locator.isVisible({ timeout: 500 }).catch(() => false)

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
}

/** Match e2e-created names: `Test Project 123` and `${PROJECT_NAME} 123`. */
function orphanProjectPatterns(): RegExp[] {
  const prefixes = new Set(["Test Project"])
  const configured = process.env.PROJECT_NAME?.trim()
  if (configured) prefixes.add(configured)
  return [...prefixes].map((prefix) => new RegExp(`${escapeRegExp(prefix)} \\d+`, "g"))
}

async function listOrphanProjectNames(page: Page): Promise<string[]> {
  const bodyText = await page.locator("body").innerText().catch(() => "")
  const names = new Set<string>()
  for (const pattern of orphanProjectPatterns()) {
    for (const match of bodyText.matchAll(pattern)) {
      names.add(match[0])
    }
  }
  return [...names]
}

function addProjectControl(page: Page) {
  return page.getByText("Add Project", { exact: true }).first()
}

/** Wait until the console project grid has painted (Add Project and/or an env chip). */
async function waitForConsoleProjectsReady(page: Page) {
  // Do not use locator.or() + toBeVisible — when both sides match, Playwright
  // strict mode fails ("resolved to 2 elements").
  await Promise.race([
    addProjectControl(page).waitFor({ state: "visible", timeout: 20_000 }),
    page.getByRole("button", { name: ENV_BUTTON }).first().waitFor({ state: "visible", timeout: 20_000 }),
  ])
}

/** Blocks console on Utilities or OS — re-authenticates when the session expired. */
export async function ensureConsole(page: Page, host: "utilities" | "os" = "utilities") {
  if (host === "os") {
    const base = e2eOsBaseUrl()
    await page.goto(`${base}/app/console`, { waitUntil: "domcontentloaded" })
    await ensureAuthenticatedOnCurrentOrigin(page)
    await expect(
      page.getByRole("heading", { name: /Your Blocks Projects|Welcome to SELISE Blocks/ }),
    ).toBeVisible({ timeout: 20_000 })
    return
  }

  await ensureAuthenticated(page)
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

const consoleProjectsHeading = (page: Page) =>
  page.getByRole("heading", { name: /Your Blocks Projects|Welcome to SELISE Blocks/ })

/** Utilities project dashboard — workspace shell visible (fails fast if bounced to console). */
export async function waitForUtilitiesDashboardReady(page: Page, projectName: string) {
  const workspace = page.getByText(/^workspace$/i)
  const bouncedToConsole = async () => {
    if (/\/app\/console\/?$/i.test(new URL(page.url()).pathname)) return true
    return consoleProjectsHeading(page).isVisible({ timeout: 500 }).catch(() => false)
  }

  // Soft redirects: URL may briefly be /dashboard then land on console.
  const outcome = await Promise.race([
    workspace.waitFor({ state: "visible", timeout: 30_000 }).then(() => "ready" as const),
    page
      .waitForURL(/\/app\/console\/?$/i, { timeout: 30_000 })
      .then(() => "console" as const)
      .catch(() => null),
    consoleProjectsHeading(page)
      .waitFor({ state: "visible", timeout: 30_000 })
      .then(() => "console" as const)
      .catch(() => null),
  ])

  if (outcome === "console" || (await bouncedToConsole())) {
    throw new Error(
      `Expected project dashboard for "${projectName}" but landed on the console. ` +
        "Suite setup must persist storageState after opening the shared project " +
        "(project/environment localStorage). Re-run utilities-setup.",
    )
  }

  if (outcome !== "ready") {
    await expect(workspace).toBeVisible({ timeout: 1_000 })
  }

  await expect(page).toHaveURL(/\/app\/(?!project\/)[^/]+\/dashboard/, { timeout: 10_000 })
  await expect(page.getByText(projectName, { exact: true }).first()).toBeVisible({ timeout: 30_000 })
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

async function freeProjectSlotIfNeeded(page: Page) {
  await ensureConsole(page, "utilities")
  await waitForConsoleProjectsReady(page)

  const welcomeHeading = page.getByRole("heading", { name: "Welcome to SELISE Blocks" })
  if (await isVisibleNow(welcomeHeading)) return

  const addProjectButton = addProjectControl(page)
  if (await isVisibleNow(addProjectButton)) return

  const atProjectLimit = page.getByText("Please delete an existing project to create a new one.")
  const limitVisible = await isVisibleNow(atProjectLimit)

  // Slot full: either the explicit limit banner, or Add Project simply missing.
  if (!limitVisible && (await addProjectButton.isVisible({ timeout: 2_000 }).catch(() => false))) {
    return
  }

  for (let attempt = 0; attempt < 8; attempt++) {
    const orphanNames = await listOrphanProjectNames(page)
    if (orphanNames.length === 0) break

    await deleteCreatedProject(page, orphanNames[0]).catch(() => {})
    await ensureConsole(page, "utilities")
    await waitForConsoleProjectsReady(page)

    if (await isVisibleNow(addProjectButton)) return
  }

  await expect(addProjectButton).toBeVisible({ timeout: 15_000 })
}

/**
 * Creates a project via the OS-hosted wizard (Utilities redirects to OS for this).
 *
 * Flow:
 *   Utilities console → "Add Project" → redirected to OS create-project wizard →
 *   fill wizard → project created on OS (environments or console) →
 *   navigate to Utilities console → open the new project → Utilities dashboard
 */
export async function createProject(page: Page) {
  await test.step("Start a new project (redirects to OS)", async () => {
    await ensureAuthenticated(page)
    await ensureConsole(page, "utilities")
    await waitForConsoleProjectsReady(page)

    const welcomeHeading = page.getByRole("heading", { name: "Welcome to SELISE Blocks" })
    const createProjectButton = page.getByRole("button", { name: "Create a project" })
    const addProjectButton = addProjectControl(page)

    await freeProjectSlotIfNeeded(page)

    if (await welcomeHeading.isVisible().catch(() => false)) {
      await createProjectButton.click()
    } else {
      await expect(addProjectButton).toBeVisible({ timeout: 15_000 })
      await addProjectButton.click()
    }
    await expect(page).toHaveURL(/\/app\/create-project$/, { timeout: 15_000 })
  })

  const baseProjectName = process.env.PROJECT_NAME?.trim() || "Test Project"
  const projectName = `${baseProjectName} ${Date.now()}`
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
    // Dev often lands on /project/{id}/environments; prod OS may send you
    // straight back to /app/console after the success toast.
    await expect(page).toHaveURL(/\/app\/(console|project\/[^/]+\/environments)\/?$/, {
      timeout: 20_000,
    })
  })

  await test.step("Return to Utilities console", async () => {
    // Prefer direct navigation over the app switcher: known destination,
    // no ambiguous text matches, no OIDC initiate race after create.
    await page.goto(`${e2eBaseUrl()}/app/console`, { waitUntil: "domcontentloaded" })
    await ensureAuthenticated(page)
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
  await waitForConsoleProjectsReady(page)

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

  // Prefer create: createProject waits for Add Project (15s) and frees orphan slots.
  // Do not gate on a short isVisible(2s) — the control can still be painting.
  try {
    const created = await createProject(page)
    const itemId = new URL(created.dashboardUrl).pathname.split("/")[2] ?? ""
    return { ...created, itemId }
  } catch (error) {
    const detail = error instanceof Error ? error.message : String(error)
    throw new Error(
      "Could not create a shared project (Add Project missing or create failed). " +
        "Set E2E_REUSE_PROJECT_NAME (e.g. test) or E2E_PROJECT_ID, or free a console slot. " +
        `Cause: ${detail}`,
    )
  }
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
