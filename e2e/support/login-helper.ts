import { expect, type Page } from "@playwright/test"
import { e2eBaseUrl, e2eCredentials } from "./env"

function oidcEmailField(page: Page) {
  return page.locator("#oidc-email").or(page.getByRole("textbox", { name: "Work Email" }))
}

function oidcPasswordField(page: Page) {
  return page.locator("#oidc-password").or(page.getByRole("textbox", { name: "Password" }))
}

const consoleHeading = (page: Page) =>
  page.getByRole("heading", {
    name: /Your Blocks Projects|Welcome to SELISE Blocks/,
  })

export async function loginThroughOidc(page: Page, options?: { loginPath?: string }) {
  const { email, password } = e2eCredentials()
  const loginPath = options?.loginPath ?? "/login"

  await page.goto(loginPath)

  if (await consoleHeading(page).isVisible({ timeout: 10_000 }).catch(() => false)) {
    return
  }

  const loginButton = page.getByRole("button", { name: "Log in to your account" })
  if (await loginButton.isVisible({ timeout: 10_000 }).catch(() => false)) {
    await loginButton.click()
  }

  const emailField = oidcEmailField(page)
  await Promise.race([
    emailField.waitFor({ state: "visible", timeout: 45_000 }),
    consoleHeading(page).waitFor({ state: "visible", timeout: 45_000 }),
    page.waitForURL((url) => url.pathname === "/" || /\/app\/console/.test(url.pathname), {
      timeout: 45_000,
    }),
  ]).catch(() => {})

  if (await consoleHeading(page).isVisible().catch(() => false)) {
    return
  }

  if (await emailField.isVisible().catch(() => false)) {
    await emailField.fill(email)
    const passwordField = oidcPasswordField(page)
    await expect(passwordField).toBeVisible({ timeout: 15_000 })
    await passwordField.fill(password)
    await page.getByRole("button", { name: "Login", exact: true }).click()
    await page.waitForURL(/\/app\/console/, { timeout: 45_000 })
    return
  }

  const origin = /^https?:/.test(page.url()) ? new URL(page.url()).origin : e2eBaseUrl()
  await page.goto(`${origin}/app/console`)
  await expect(consoleHeading(page)).toBeVisible({ timeout: 45_000 })
}

export async function ensureAuthenticated(page: Page) {
  await page.goto(`${e2eBaseUrl()}/app/console`)

  if (await consoleHeading(page).isVisible({ timeout: 30_000 }).catch(() => false)) {
    return
  }

  await loginThroughOidc(page)
}

export async function ensureAuthenticatedOnCurrentOrigin(page: Page) {
  const href = page.url()
  if (!/^https?:/.test(href)) {
    await ensureAuthenticated(page)
    return
  }

  const origin = new URL(href).origin
  await page.goto(`${origin}/app/console`)

  if (await consoleHeading(page).isVisible({ timeout: 30_000 }).catch(() => false)) {
    return
  }

  await loginThroughOidc(page, { loginPath: `${origin}/login` })
}
