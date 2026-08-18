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
  await page.getByRole("button", { name: "Log in to your account" }).click()

  const emailField = oidcEmailField(page)
  await emailField.waitFor({ state: "visible", timeout: 45_000 })
  await emailField.fill(email)

  const passwordField = oidcPasswordField(page)
  await expect(passwordField).toBeVisible({ timeout: 15_000 })
  await passwordField.fill(password)

  await page.getByRole("button", { name: "Login", exact: true }).click()
  await page.waitForURL(/\/app\/console/, { timeout: 45_000 })
}

export async function ensureAuthenticated(page: Page) {
  await page.goto(`${e2eBaseUrl()}/app/console`)

  if (await consoleHeading(page).isVisible({ timeout: 8_000 }).catch(() => false)) {
    return
  }

  await loginThroughOidc(page)
}
