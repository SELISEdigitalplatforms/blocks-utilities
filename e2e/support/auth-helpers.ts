import { expect, type Page } from "@playwright/test";
import dotenv from "dotenv";
import path from "path";

// Loads e2e/.env.e2e (gitignored) -> E2E_BASE_URL, E2E_USERNAME, E2E_PASSWORD.
// Copy e2e/.env.e2e.example to e2e/.env.e2e and fill in real values.
dotenv.config({ path: path.resolve(__dirname, "../.env.e2e") });

const E2E_BASE_URL = process.env.E2E_BASE_URL;
const E2E_USERNAME = process.env.E2E_USERNAME;
const E2E_PASSWORD = process.env.E2E_PASSWORD;

/**
 * Logs in a fresh (unauthenticated) page against the Blocks Utilities
 * environment and lands on /app/console. Captcha is disabled on dev, so no
 * captcha handling is required here.
 */
export async function loginFresh(page: Page) {
  const email = E2E_USERNAME;
  const password = E2E_PASSWORD;
  const baseUrl = E2E_BASE_URL;

  if (!email || !password) {
    throw new Error("E2E_USERNAME/E2E_PASSWORD are not set. Set them in e2e/.env.e2e.");
  }
  if (!baseUrl) {
    throw new Error("E2E_BASE_URL is not set. Set it in e2e/.env.e2e.");
  }

  await page.goto(baseUrl, { waitUntil: "domcontentloaded", timeout: 60_000 });
  await page.getByRole("button", { name: "Log in to your account" }).click();

  const emailInput = page.getByRole("textbox", { name: "Work Email" });
  const passwordInput = page.getByRole("textbox", { name: "Password" });

  await emailInput.fill(email);
  await passwordInput.fill(password);
  await page.getByRole("button", { name: "Login" }).click();
  await page.waitForURL(/\/app\/console/, { timeout: 60_000, waitUntil: "domcontentloaded" });
}

/**
 * Opens the first project from the console by clicking its environment chip
 * (e.g. "Development"), then waits for the project workspace shell
 * (sidebar with WORKSPACE/PROJECT/ENVIRONMENT + Overview/Deployment nav)
 * to render.
 */
export async function openFirstProject(page: Page) {
  await page
    .getByRole("button", { name: /Development|Testing|Staging|IAT|UAT|Production/ })
    .first()
    .click();
  await expect(page.getByText(/^workspace$/i)).toBeVisible({
    timeout: 50_000,
  });
}

/** Sidebar nav item: rendered as either a link or a button by the shell. */
export function sidebarNavItem(
  page: Page,
  name:
    | "Overview"
    | "Create Payment"
    | "Payment List"
    | "Saved Cards"
    | "Payment Providers"
    | "Payments"
    | "Magic URL",
) {
  return page
    .getByRole("link", { name, exact: true })
    .or(page.getByRole("button", { name, exact: true }));
}

/** Expands the "Payments" sidebar group if its children aren't visible yet. */
export async function openPaymentsSubPage(
  page: Page,
  name: "Overview" | "Create Payment" | "Payment List" | "Saved Cards" | "Payment Providers",
) {
  const subLink = sidebarNavItem(page, name);
  if (
    !(await subLink
      .first()
      .isVisible()
      .catch(() => false))
  ) {
    await sidebarNavItem(page, "Payments").first().click();
    await expect(subLink.first()).toBeVisible();
  }
  await subLink.first().click();
}
