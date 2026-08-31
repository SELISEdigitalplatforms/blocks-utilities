import { test, expect } from "@playwright/test"
import fs from "fs"
import path from "path"
import { reuseOrCreateSharedProject } from "../../support/create-and-delete-project"
import { loginThroughOidc } from "../../support/login-helper"
import { UTILITIES_SESSION_PATH, writeUtilitiesProject } from "../../support/utilities-project"
import { resetRunOutcome } from "../../support/run-outcome"

test.describe("utilities suite setup", () => {
  test("login, reuse or create one shared project", async ({ page }) => {
    test.setTimeout(300_000)
    resetRunOutcome()

    await loginThroughOidc(page)
    await expect(
      page.getByRole("heading", { name: /Your Blocks Projects|Welcome to SELISE Blocks/ }),
    ).toBeVisible({ timeout: 30_000 })

    const { projectName, dashboardUrl, itemId } = await reuseOrCreateSharedProject(page)
    if (!itemId) {
      throw new Error(`Could not resolve itemId from dashboard URL: ${dashboardUrl}`)
    }

    writeUtilitiesProject({
      projectName,
      itemId,
      dashboardUrl: dashboardUrl.replace(/\?.*$/, ""),
    })

    // Persist AFTER the shared project is open so localStorage keeps the selected
    // project/environment. Saving only post-login makes /app/{id}/dashboard bounce
    // back to /app/console in feature tests.
    fs.mkdirSync(path.dirname(UTILITIES_SESSION_PATH), { recursive: true })
    await page.context().storageState({ path: UTILITIES_SESSION_PATH })
  })
})
