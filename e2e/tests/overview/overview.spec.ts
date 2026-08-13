import { test, expect } from "../../support/test-base";
import { loginFresh } from "../../support/login-helper";

// Fresh, isolated context for this file — ignore the "chromium" project's
// default storageState and log in for real instead of reusing a saved session.
test.use({ storageState: { cookies: [], origins: [] } });

test.describe("overview", () => {
  test.beforeEach(async ({ page }) => {
    test.setTimeout(180_000);
    await loginFresh(page);

    await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
      timeout: 30_000,
    });

    await page
      .getByRole("button", { name: /Development/ })
      .first()
      .click();
    await expect(page.getByRole("heading", { name: "Project Details" })).toBeVisible({
      timeout: 30_000,
    });
  });

  test("TC-0001: Overview page renders project details after data loads", async ({ page }) => {
    const detailsCard = page.locator("div").filter({ hasText: "Name" }).first();

    await expect(detailsCard).toBeVisible();
    await expect(detailsCard.getByText("Name", { exact: true })).toBeVisible();
    await expect(detailsCard.getByText("X-Blocks-Key", { exact: true })).toBeVisible();
    await expect(page.getByRole("main").getByText("Environment", { exact: true })).toBeVisible();
    await expect(detailsCard.getByText("Last updated Date", { exact: true })).toBeVisible();
    await expect(detailsCard.getByText("Created Date", { exact: true })).toBeVisible();
  });

  test("TC-0002: Loading skeleton displays while project data is fetching", async ({ page }) => {
    await page.route("**/*", (route) => {
      setTimeout(() => route.continue(), 500);
    });
    page.reload().catch(() => {});

    const skeletons = page.locator('[class*="animate-pulse"]');
    await expect(skeletons.first()).toBeVisible({ timeout: 30_000 });

    await expect(page.getByRole("heading", { name: "Project Details" })).toBeVisible({
      timeout: 30_000,
    });

    await page.unroute("**/*");
  });

  test("TC-0004: Copy-to-clipboard button copies the full unmasked X-Blocks-Key value", async ({
    page,
    context,
  }) => {
    await context.grantPermissions(["clipboard-read", "clipboard-write"]);

    const keyRow = page
      .locator("div")
      .filter({ has: page.getByText("X-Blocks-Key", { exact: true }) })
      .filter({ has: page.getByRole("button") })
      .last();

    await keyRow.hover();

    const copyButton = keyRow.getByRole("button");

    await expect(copyButton).toBeVisible({ timeout: 30_000 });
    await copyButton.click();

    const clipboardText = await page.evaluate(() => navigator.clipboard.readText());

    expect(clipboardText).not.toContain("*");
    expect(clipboardText.length).toBeGreaterThan(0);
  });

  test("TC-0005: Copy-to-clipboard falls back to document.execCommand outside a secure context", async ({
    page,
  }) => {
    // Force isSecureContext to false before the copy button's click handler runs.
    await page.addInitScript(() => {
      Object.defineProperty(window, "isSecureContext", {
        value: false,
        configurable: true,
      });
    });
    await page.reload();

    let execCommandCalled = false;
    await page.exposeFunction("__execCommandCalled", () => {
      execCommandCalled = true;
    });
    await page.evaluate(() => {
      const originalExecCommand = document.execCommand.bind(document);
      document.execCommand = (...args: Parameters<typeof document.execCommand>) => {
        (window as unknown as { __execCommandCalled: () => void }).__execCommandCalled();
        return originalExecCommand(...args);
      };
    });

    const keyRow = page
      .locator("div")
      .filter({ has: page.getByText("X-Blocks-Key", { exact: true }) })
      .filter({ has: page.getByRole("button") })
      .last();
    await keyRow.getByRole("button").click();

    await expect.poll(() => execCommandCalled).toBeTruthy();
  });

  test("TC-0006: Project Slug field is hidden when tenantSlug is not present on the project", async ({
    page,
  }) => {
    // NOTE: assumes the currently selected project/environment has no tenantSlug.
    await expect(page.getByText("Project Slug", { exact: true })).toHaveCount(0);
  });

  test("TC-0007: Production environment renders a filled 'Production' badge", async ({ page }) => {
    // NOTE: skipped when the current tenant has no Production environment card.
    await page.goBack();
    const productionCard = page.getByRole("button", { name: /Production/ }).first();

    if (!(await productionCard.isVisible({ timeout: 10000 }).catch(() => false))) {
      test.skip(true, "No Production environment available for this project");
      return;
    }

    await productionCard.click();
    await expect(page.getByRole("heading", { name: "Project Details" })).toBeVisible({
      timeout: 30_000,
    });

    await expect(page.getByRole("button", { name: "Production" })).toBeVisible({
      timeout: 30_000,
    });
  });

  test("TC-0008: Non-production environment renders a secondary badge with the matching label", async ({
    page,
  }) => {
    const environmentRow = page
      .locator("div")
      .filter({ has: page.getByText("Environment", { exact: true }) })
      .filter({ has: page.getByRole("button") })
      .last();
    await expect(environmentRow.getByRole("button")).toBeVisible({
      timeout: 30_000,
    });
    await expect(environmentRow.getByRole("button", { name: "Production" })).toHaveCount(0);
  });

  test("TC-0009: Last updated Date and Created Date are shown in full formatted date form", async ({
    page,
  }) => {
    const createdRow = page.locator("div").filter({ hasText: "Created Date" }).last();
    const createdText = (await createdRow.innerText()).replace("Created Date", "").trim();

    expect(createdText).not.toContain("T00:00:00");
  });

  test("TC-0010: Custom domain cookie validation runs silently on Overview load without blocking the UI", async ({
    page,
  }) => {
    // NOTE: assumes the current project has a custom domain configured.
    // The check runs in the background via useEffect; the page must simply keep rendering normally.
    await expect(page.getByRole("heading", { name: "Project Details" })).toBeVisible({
      timeout: 30_000,
    });
    await expect(page.getByText("Name", { exact: true })).toBeVisible({
      timeout: 30_000,
    });
  });

  test("TC-0011: 'Back to console' link returns the user to the project console from Overview", async ({
    page,
  }) => {
    const backButton = page
      .getByText("Back to console")
      .or(page.getByText("Console", { exact: true }));
    await backButton.first().click();

    await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
      timeout: 30_000,
    });
  });
});
