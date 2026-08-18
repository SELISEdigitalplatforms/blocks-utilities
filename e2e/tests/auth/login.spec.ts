import { createProject } from "../../support/create-and-delete-project";
import { test, expect } from "../../support/test-base";

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;

test.describe("Authentication", () => {
  test.beforeAll(() => {
    if (!username || !password) {
      throw new Error(
        "E2E_USERNAME / E2E_PASSWORD are not set. Fill them in e2e/.env.e2e before running.",
      );
    }
  });

  test("logs in through dev-iam and lands on the console", async ({ page }) => {
    // Extend the test timeout to cover an optional inspection hold at the end.
    const holdMs = Number(process.env.E2E_HOLD_MS ?? 0);
    if (holdMs > 0) test.setTimeout(holdMs + 60_000);

    // 1. Blocks Utilities login page — a single CTA that starts the OIDC flow.
    await page.goto("/login");
    await page.getByRole("button", { name: "Log in to your account" }).click();

    // 2. Redirected to the dev-iam OIDC login page (/oidc/login, cross-origin).
    //    Selectors come from blocks-idp oidc-login-form.tsx (stable field ids).
    const emailField = page.locator("#oidc-email");
    await emailField.waitFor({ timeout: 30_000 });
    await emailField.fill(username!);
    await page.locator("#oidc-password").fill(password!);
    await page.getByRole("button", { name: "Login", exact: true }).click();

    // 3. Optional one-time OIDC consent/permission screen.
    //    The OIDC path can route through /oidc/permission before returning.
    //    Confirm the real button label via `npm run codegen` on the first live
    //    run, then enable this block if the screen appears.
    // const consentBtn = page.getByRole("button", {
    //   name: /allow|authorize|continue|grant/i,
    // });
    // if (await consentBtn.isVisible().catch(() => false)) {
    //   await consentBtn.click();
    // }

    // 4. Back on Blocks Utilities, authenticated → console.
    await page.waitForURL("**/app/console", { timeout: 45_000 });
    await expect(page).toHaveURL(/\/app\/console/);

    // Assert the console actually rendered — not just that the route changed.
    // This repo renders <ConsolePage /> without `canCreateProject`, and the kit
    // defaults it to false, so the "Welcome to SELISE Blocks" empty state is
    // unreachable here. Only "Your Blocks Projects" can appear.
    await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
      timeout: 30_000,
    });

    await createProject(page);

    await page.getByRole("button", { name: "Open user menu" }).click();
    await page.getByText("Log out").click();
    await expect(page.getByRole("heading", { name: "blocks Utilities" })).toBeVisible({
      timeout: 30_000,
    });

    // Persist the authenticated session for future specs to reuse.
    await page.context().storageState({ path: "fixtures/auth.json" });

    // Optionally keep the browser open to inspect the result before it closes.
    // e.g. E2E_HOLD_MS=120000 npm run test:headed
    if (holdMs > 0) {
      await page.waitForTimeout(holdMs);
    }
  });
});
