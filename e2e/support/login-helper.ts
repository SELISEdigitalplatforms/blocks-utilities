import type { Page } from "@playwright/test";

const baseUrl = process.env.E2E_BASE_URL;
const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;

export async function loginFresh(page: Page) {
  await page.goto(`${baseUrl}/login`);
  await page.waitForLoadState("domcontentloaded");

  // The login CTA can render before its OIDC click handler is wired (hydration
  // race on a cold SPA load) and the cross-origin redirect is occasionally
  // slow, so retry the click until the OIDC email field appears.
  const loginCta = page.getByRole("button", { name: "Log in to your account" });
  await loginCta.waitFor({ state: "visible", timeout: 60_000 });
  const emailField = page.locator("#oidc-email");
  let reachedOidc = false;
  for (let attempt = 0; attempt < 4 && !reachedOidc; attempt++) {
    if (await loginCta.isVisible().catch(() => false)) {
      await loginCta.click().catch(() => {});
    }
    reachedOidc = await emailField
      .waitFor({ state: "visible", timeout: 20_000 })
      .then(() => true)
      .catch(() => false);
  }

  await emailField.waitFor({ timeout: 20_000 });
  await emailField.fill(username!);
  await page.locator("#oidc-password").fill(password!);
  await page.getByRole("button", { name: "Login", exact: true }).click();

  await page.waitForURL(/\/app\/console/, { timeout: 45_000 });
}
