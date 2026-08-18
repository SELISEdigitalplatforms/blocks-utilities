import { test } from "@playwright/test"
import { deleteCreatedProject } from "../support/create-and-delete-project"
import { clearUtilitiesProject, readUtilitiesProject } from "../support/utilities-project"

test.describe("utilities teardown", () => {
  test("delete shared utilities project", async ({ page }) => {
    const fixture = readUtilitiesProject()
    if (!fixture?.projectName) return

    await deleteCreatedProject(page, fixture.projectName).catch(() => {})
    clearUtilitiesProject()
  })
})
