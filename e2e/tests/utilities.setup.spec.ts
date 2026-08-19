import { test, expect } from "@playwright/test"
import { loginThroughOidc } from "../support/login-helper"
import { createProject } from "../support/create-and-delete-project"
import {
  UTILITIES_SESSION_PATH,
  writeUtilitiesProject,
} from "../support/utilities-project"

test.describe("utilities setup", () => {
  test("login once and create shared project for utilities tests", async ({ page }) => {
    await loginThroughOidc(page)
    await expect(
      page.getByRole("heading", { name: /Your Blocks Projects|Welcome to SELISE Blocks/ }),
    ).toBeVisible({ timeout: 50_000 })

    const { projectName } = await createProject(page)
    const itemId = new URL(page.url()).pathname.split("/")[2] ?? ""
    if (!itemId) {
      throw new Error(`Could not resolve itemId from dashboard URL: ${page.url()}`)
    }

    writeUtilitiesProject({
      projectName,
      itemId,
      dashboardUrl: page.url(),
    })
    await page.context().storageState({ path: UTILITIES_SESSION_PATH })
  })
})
