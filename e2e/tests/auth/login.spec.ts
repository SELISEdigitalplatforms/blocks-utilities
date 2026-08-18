import { test, expect } from "../../support/test-base";
import { loginThroughOidc } from "../../support/login-helper";

test.describe("Authentication", () => {
  test("logs in through dev-iam and lands on the console", async ({ page }) => {
    const holdMs = Number(process.env.E2E_HOLD_MS ?? 0);
    if (holdMs > 0) test.setTimeout(holdMs + 120_000);

    await loginThroughOidc(page);

    await expect(
      page.getByRole("heading", {
        name: /Your Blocks Projects|Welcome to SELISE Blocks/,
      }),
    ).toBeVisible({ timeout: 30_000 });

    await page.context().storageState({ path: "fixtures/auth.json" });

    await page.getByRole("button", { name: "Open user menu" }).click();
    await page.getByText("Log out").click();
    await expect(page.getByRole("heading", { name: "blocks Utilities" })).toBeVisible({
      timeout: 30_000,
    });

    if (holdMs > 0) {
      await page.waitForTimeout(holdMs);
    }
  });
});
