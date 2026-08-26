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

/** True when the page is the product login gate or OIDC credential form. */
export async function isLoginSurface(page: Page): Promise<boolean> {
  if (
    await page
      .getByRole("button", { name: "Log in to your account" })
      .isVisible({ timeout: 500 })
      .catch(() => false)
  ) {
    return true
  }

  if (await oidcEmailField(page).isVisible({ timeout: 500 }).catch(() => false)) {
    return true
  }

  try {
    if (/\/login\/?$/i.test(new URL(page.url()).pathname)) return true
  } catch {
    // ignore invalid URL
  }

  return false
}

async function fillCredentialsAndSubmit(page: Page) {
  const { email, password } = e2eCredentials()
  const emailField = oidcEmailField(page)
  await emailField.fill(email)
  const passwordField = oidcPasswordField(page)
  await expect(passwordField).toBeVisible({ timeout: 10_000 })
  await passwordField.fill(password)
  await page.getByRole("button", { name: "Login", exact: true }).click()
}

export async function loginThroughOidc(page: Page, options?: { loginPath?: string }) {
  const base = e2eBaseUrl()
  const loginPath = options?.loginPath ?? `${base}/login`

  await page.goto(loginPath, { waitUntil: "domcontentloaded" })

  for (let attempt = 0; attempt < 3; attempt++) {
    if (await consoleHeading(page).isVisible({ timeout: 3_000 }).catch(() => false)) {
      return
    }

    const loginButton = page.getByRole("button", { name: "Log in to your account" })
    if (await loginButton.isVisible({ timeout: 3_000 }).catch(() => false)) {
      try {
        await loginButton.click({ timeout: 8_000 })
      } catch {
        if (await consoleHeading(page).isVisible({ timeout: 3_000 }).catch(() => false)) return
        await page.goto(`${base}/app/console`, { waitUntil: "domcontentloaded" })
        continue
      }

      const emailField = oidcEmailField(page)
      await Promise.race([
        emailField.waitFor({ state: "visible", timeout: 30_000 }),
        consoleHeading(page).waitFor({ state: "visible", timeout: 30_000 }),
        page.waitForURL(/\/app\/console/, { timeout: 30_000 }),
      ]).catch(() => {})

      if (await consoleHeading(page).isVisible().catch(() => false)) {
        return
      }

      if (await emailField.isVisible().catch(() => false)) {
        await fillCredentialsAndSubmit(page)
        await page.waitForURL(/\/app\/console/, { timeout: 45_000 })
        return
      }

      await page.goto(`${base}/app/console`, { waitUntil: "domcontentloaded" })
      continue
    }

    await page.goto(`${base}/app/console`, { waitUntil: "domcontentloaded" })
  }

  await page.goto(`${base}/app/console`, { waitUntil: "domcontentloaded" })
  await expect(consoleHeading(page)).toBeVisible({ timeout: 30_000 })
}

/**
 * Land on the product console; re-run OIDC when the saved session expired.
 * Idempotent when already authenticated.
 */
export async function ensureAuthenticated(page: Page) {
  const base = e2eBaseUrl()
  await page.goto(`${base}/app/console`, { waitUntil: "domcontentloaded" })

  if (await consoleHeading(page).isVisible({ timeout: 15_000 }).catch(() => false)) {
    return
  }

  await loginThroughOidc(page)
  await expect(consoleHeading(page)).toBeVisible({ timeout: 30_000 })
}

export async function ensureAuthenticatedOnCurrentOrigin(page: Page) {
  const href = page.url()
  if (!/^https?:/.test(href)) {
    await ensureAuthenticated(page)
    return
  }

  const origin = new URL(href).origin
  await page.goto(`${origin}/app/console`, { waitUntil: "domcontentloaded" })

  if (await consoleHeading(page).isVisible({ timeout: 15_000 }).catch(() => false)) {
    return
  }

  await loginThroughOidc(page, { loginPath: `${origin}/login` })
  await expect(consoleHeading(page)).toBeVisible({ timeout: 30_000 })
}

export async function loginFresh(page: Page) {
  await loginThroughOidc(page, { loginPath: e2eBaseUrl() })
}
