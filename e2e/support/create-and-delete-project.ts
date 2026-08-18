import { Page, expect, test } from "@playwright/test";

export async function createProject(page: Page) {
  // ---------- Open the create-project wizard ----------
  await test.step("Start a new project", async () => {
    const welcomeHeading = page.getByRole("heading", {
      name: "Welcome to SELISE Blocks",
    });
    const createProjectButton = page.getByRole("button", {
      name: "Create a project",
    });
    const addProjectButton = page.getByText("Add Project", { exact: true }).first();

    // Wait for whichever landing state actually renders instead of racing
    // an isVisible() poll against a page that may still be loading.
    await Promise.race([
      welcomeHeading.waitFor({ state: "visible", timeout: 50000 }),
      addProjectButton.waitFor({ state: "visible", timeout: 50000 }),
    ]);

    if (await welcomeHeading.isVisible().catch(() => false)) {
      await createProjectButton.click();
    } else {
      await addProjectButton.click();
    }
    await expect(page).toHaveURL(/\/app\/create-project$/, { timeout: 15000 });
  });

  // ---------- Step 1: Name your project ----------
  await test.step("Step 1 — 'Continue' is disabled until a valid name and both agreements are given", async () => {
    await expect(page.getByRole("heading", { name: "Name your project" })).toBeVisible({
      timeout: 30_000,
    });
    const continueButton = page.getByRole("button", {
      name: "Continue",
      exact: true,
    });
    await expect(continueButton).toBeDisabled();

    const nameInput = page.locator('[placeholder="Enter your project name"]:visible');
    await nameInput.fill("ab");
    await expect(page.getByText("Project name must be at least 3 characters")).toBeVisible({
      timeout: 30_000,
    });
    await expect(continueButton).toBeDisabled();

    await nameInput.fill("a".repeat(101));
    await expect(page.getByText("Project name should be a maximum of 100 characters")).toBeVisible({
      timeout: 30_000,
    });
    await expect(continueButton).toBeDisabled();
  });

  const projectName = `Test Project ${Date.now()}`;
  await test.step("Step 1 — filling a valid name and accepting both agreements enables Continue", async () => {
    const nameInput = page.locator('[placeholder="Enter your project name"]:visible');
    await nameInput.fill(projectName);

    const continueButton = page.getByRole("button", {
      name: "Continue",
      exact: true,
    });
    await expect(continueButton).toBeDisabled();

    await page.getByRole("checkbox", { name: "I confirm that I will use" }).click();
    await expect(continueButton).toBeDisabled();

    await page.getByRole("checkbox", { name: "I accept the Terms of services" }).click();
    await expect(continueButton).toBeEnabled();

    await continueButton.click();
  });

  // ---------- Step 2: Add resources ----------
  await test.step("Step 2 — 'Add resource' allows continuing without linking a repository", async () => {
    await expect(page.getByRole("heading", { name: "Add resource" })).toBeVisible({
      timeout: 30_000,
    });

    // Repositories are optional — Continue should already be enabled with none selected.
    const continueButton = page.getByRole("button", {
      name: "Continue",
      exact: true,
    });
    await expect(continueButton).toBeEnabled();
  });

  await test.step("Step 2 — 'Add repository' opens the provider-connection dialog when not yet authorized", async () => {
    const addRepoButton = page.getByRole("button", { name: "Add repository" });
    await addRepoButton.click();

    const connectDialog = page.getByRole("heading", {
      name: "Connect repository",
    });
    const selectDialog = page.getByRole("heading", {
      name: "Select repository",
    });
    await expect(connectDialog.or(selectDialog)).toBeVisible({
      timeout: 30_000,
    });

    if (await connectDialog.isVisible().catch(() => false)) {
      await expect(
        page.getByText(
          "Select a Git provider to import an existing project from a Git Repository.",
        ),
      ).toBeVisible({ timeout: 30_000 });
    }
    // Close without connecting — repositories are optional for this flow.
    await page.keyboard.press("Escape");
  });

  await test.step("Step 2 — proceed to the next step without adding a repository", async () => {
    await page.getByRole("button", { name: "Continue", exact: true }).click();
  });

  // ---------- Step 3: Select environments ----------
  await test.step("Step 3 — 'Submit' is disabled until at least one environment is selected", async () => {
    await expect(
      page.getByText("Select environments", { exact: true }).and(page.locator(":visible")),
    ).toBeVisible({ timeout: 30_000 });
    await expect(
      page
        .getByText(
          "Please ensure that the branch name in your Git repository matches the environment's label exactly",
        )
        .and(page.locator(":visible")),
    ).toBeVisible({ timeout: 30_000 });

    const submitButton = page.getByRole("button", { name: "Submit" });
    await expect(submitButton).toBeDisabled();
  });

  await test.step("Step 3 — all 8 environment options are offered with their branch hints", async () => {
    await expect(
      page.getByText("Development", { exact: true }).and(page.locator(":visible")),
    ).toBeVisible({ timeout: 30_000 });
    await expect(
      page.getByText("Testing", { exact: true }).and(page.locator(":visible")),
    ).toBeVisible({
      timeout: 30_000,
    });
    await expect(
      page.getByText("Staging", { exact: true }).and(page.locator(":visible")),
    ).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText("IAT", { exact: true }).and(page.locator(":visible"))).toBeVisible({
      timeout: 30_000,
    });
    await expect(page.getByText("UAT", { exact: true }).and(page.locator(":visible"))).toBeVisible({
      timeout: 30_000,
    });
    await expect(
      page.getByText("Prod Shadow", { exact: true }).and(page.locator(":visible")),
    ).toBeVisible({ timeout: 30_000 });
    await expect(
      page.getByText("Pre-Prod", { exact: true }).and(page.locator(":visible")),
    ).toBeVisible({ timeout: 30_000 });
    await expect(
      page.getByText("Production", { exact: true }).and(page.locator(":visible")),
    ).toBeVisible({ timeout: 30_000 });
  });

  await test.step("Step 3 — selecting Development enables Submit", async () => {
    await page.getByText("Development", { exact: true }).and(page.locator(":visible")).click();
    await expect(page.getByRole("button", { name: "Submit" })).toBeEnabled();
  });

  // ---------- Final submission ----------
  await test.step("Submitting creates the project and redirects into its Environments page", async () => {
    await page.getByRole("button", { name: "Submit" }).click();

    await expect(page.getByText("Your project has been created.", { exact: true })).toBeVisible({
      timeout: 30_000,
    });
    await expect(page).toHaveURL(/\/app\/project\/[^/]+\/environments$/, {
      timeout: 20000,
    });
  });

  await test.step("Stay on the console page for 30 seconds", async () => {
    await page.goto(`${process.env.E2E_BASE_URL}/app/console`);
    await page.waitForTimeout(10_000);
  });

  return { projectName };
}

export async function deleteProject(page: Page) {
  let projectName = "";

  // ---------- Return to console before selecting the project ----------
  await test.step("Return to console before deleting the project", async () => {
    const backToConsole = page.getByRole("button", {
      name: "Back to console",
    });

    if (await backToConsole.isVisible({ timeout: 5000 }).catch(() => false)) {
      await backToConsole.click();

      await expect(
        page.getByRole("heading", {
          name: "Your Blocks Projects",
        }),
      ).toBeVisible({
        timeout: 30000,
      });
    } else if (!/\/app\/console$/.test(page.url())) {
      const match = new URL(page.url()).pathname.match(/^\/app\/[^/]+/);

      if (match) {
        await page.goto(`${new URL(page.url()).origin}${match[0]}/console`);

        await expect(
          page.getByRole("heading", {
            name: "Your Blocks Projects",
          }),
        ).toBeVisible({
          timeout: 30000,
        });
      }
    }
  });

  // ---------- Pick an existing project from the list ----------
  await test.step("Select an existing project from 'Your Blocks Projects'", async () => {
    await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
      timeout: 30000,
    });
    const projects = page.getByRole("button", { name: /Development|Production/ });
    console.log("Project buttons found:", await projects.count());
    await expect(projects.first()).toBeVisible({ timeout: 30000 });
    const firstProject = projects.first();
    projectName = (await firstProject.innerText()).trim();
    await firstProject.click();

    await expect(page).toHaveURL(/\/app\/[^/]+\/dashboard/, {
      timeout: 30000,
    });

    await expect(page.getByText("X-Blocks-Key:")).toBeVisible({
      timeout: 15000,
    });
  });

  // ---------- Delete button is visible to the owner on an active project ----------
  await test.step("'Delete' is visible on the Overview page for the project owner", async () => {
    await expect(
      page.getByRole("button", {
        name: "Delete",
        exact: true,
      }),
    ).toBeVisible({
      timeout: 30000,
    });
  });

  // ---------- Clicking Delete opens the exact confirmation copy ----------
  await test.step("Clicking 'Delete' opens the exact confirmation dialog", async () => {
    await page
      .getByRole("button", {
        name: "Delete",
        exact: true,
      })
      .click();

    await expect(
      page.getByRole("heading", {
        name: "Delete this environment?",
      }),
    ).toBeVisible();

    await expect(page.getByText("Are you sure you want to delete this environment?")).toBeVisible();

    await expect(
      page.getByText(
        "This will permanently delete the environment and you'll need to contact support to recover it.",
      ),
    ).toBeVisible();

    await expect(
      page.getByRole("button", {
        name: "Cancel",
      }),
    ).toBeVisible();

    await expect(
      page
        .getByRole("button", {
          name: "Delete",
          exact: true,
        })
        .last(),
    ).toBeVisible();
  });

  // ---------- Confirming delete removes the project and redirects ----------
  await test.step("Confirming delete shows the success toast and redirects to the console", async () => {
    const confirmDeleteButton = page
      .getByRole("button", {
        name: "Delete",
        exact: true,
      })
      .last();

    await confirmDeleteButton.click();

    await expect(
      page.getByText("Successfully deleted", {
        exact: true,
      }),
    ).toBeVisible({
      timeout: 20000,
    });

    await expect(page).toHaveURL(/\/app\/console$/, {
      timeout: 20000,
    });
  });

  // ---------- Deleted project no longer appears in the project list ----------
  await test.step("The deleted project no longer appears in the project list", async () => {
    if (projectName) {
      await expect(page.getByText(projectName)).toHaveCount(0);
    }
  });

  return { projectName };
}
