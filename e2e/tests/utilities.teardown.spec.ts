import { test } from "@playwright/test"
import { deleteCreatedProject } from "../support/create-and-delete-project"
import { ensureAuthenticated } from "../support/login-helper"
import { clearUtilitiesProject, readUtilitiesProject } from "../support/utilities-project"

test.describe("utilities teardown", () => {
  test("delete shared utilities project", async ({ page }) => {
    test.setTimeout(180_000)
    const fixture = readUtilitiesProject()

    await ensureAuthenticated(page)
    await deleteCreatedProject(page, fixture?.projectName)
    clearUtilitiesProject()
  })
})
