import { test, expect } from "@playwright/test";
import { loginFresh, openFirstProject, sidebarNavItem } from "../../support/auth-helpers";

/**
 * Console + Project Overview ("Project Details" / "Core APIs").
 *
 * Auth: each test logs in fresh (no shared storageState) so the file stays
 * fully self-contained, matching profile.spec.ts.
 */
test.describe("Console & Project Overview", () => {
  test.beforeEach(async ({ page }) => {
    await loginFresh(page);
  });

  test("Console - topbar", async ({ page }) => {
    // Theme switcher (Auto/Light/Dark tabs). Only the active tab's text label
    // is visible in the DOM (others are display:none), so the tablist is
    // located via the "Light" tab, which is the default/active theme.
    const themeTablist = page.getByRole("tablist").first();

    const autoTab = themeTablist.locator('[aria-controls$="-content-system"]');
    const lightTab = themeTablist.locator('[aria-controls$="-content-light"]');
    const darkTab = themeTablist.locator('[aria-controls$="-content-dark"]');

    const activeTab = themeTablist.locator('[role="tab"][data-state="active"]');

    await test.step("[Positive] theme switcher offers Auto/Light/Dark and has an active theme", async () => {
      await expect(themeTablist).toBeVisible({ timeout: 30_000 });
      await expect(autoTab).toBeVisible({ timeout: 30_000 });
      await expect(lightTab).toBeVisible({ timeout: 30_000 });
      await expect(darkTab).toBeVisible({ timeout: 30_000 });
      await expect(activeTab).toHaveCount(1);
    });

    await test.step("[Positive] switching to Dark applies the dark theme", async () => {
      await darkTab.click();
      await expect(darkTab).toHaveAttribute("data-state", "active");
      await expect(page.locator("html")).toHaveClass(/dark/);
      // Restore Light so the rest of the suite sees a consistent theme.
      await lightTab.click();
      await expect(lightTab).toHaveAttribute("data-state", "active");
    });

    await test.step("[Positive] language selector shows EN and lists English/German/French", async () => {
      const languageButton = page.getByRole("button", { name: /^en$/i });
      await expect(languageButton).toBeVisible();
      await languageButton.click();
      await expect(page.getByRole("menuitem", { name: "English" })).toBeVisible();
      await expect(page.getByRole("menuitem", { name: "German" })).toBeVisible();
      await expect(page.getByRole("menuitem", { name: "French" })).toBeVisible();
      await page.keyboard.press("Escape");
    });

    await test.step("[Negative] German and French are disabled (English-only environment)", async () => {
      const languageButton = page.getByRole("button", { name: /^en$/i });
      await languageButton.click();
      await expect(page.getByRole("menuitem", { name: "German" })).toHaveAttribute(
        "aria-disabled",
        "true",
      );
      await expect(page.getByRole("menuitem", { name: "French" })).toHaveAttribute(
        "aria-disabled",
        "true",
      );
      await page.keyboard.press("Escape");
    });

    await test.step("[Positive] notification bell opens the notifications popover", async () => {
      await page.getByTestId("notification-bell").click();
      await expect(page.getByText("Notifications", { exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: "Mark all as read" })).toBeVisible();
      await page.keyboard.press("Escape");
    });

    await test.step("[Positive] app switcher (grid icon) opens the SELISE Blocks apps list", async () => {
      await page.getByRole("button", { name: "SELISE Blocks apps" }).click();
      await expect(page.getByText("SELISE Blocks", { exact: true })).toBeVisible();
      await page.keyboard.press("Escape");
    });

    await test.step("[Security] user menu exposes account info and a Log out action (not clicked)", async () => {
      await page.getByRole("button", { name: "Open user menu" }).click();
      await expect(page.getByText("Log out", { exact: true })).toBeVisible();
      // Intentionally not clicking Log out - it would end the session mid-suite.
      await page.keyboard.press("Escape");
    });
  });

  test("Console - project list", async ({ page }) => {
    await test.step("[Positive] Your Blocks Projects section lists at least one project", async () => {
      await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible();
      await expect(page.getByText("Add Project", { exact: true })).toBeVisible();
    });

    await test.step("[Positive] Resources section (Docs/Code/Cloud) is visible", async () => {
      await expect(page.getByText("Resources", { exact: true })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Docs", exact: true })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Code", exact: true })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Cloud", exact: true })).toBeVisible();
    });
  });

  test("Project Overview - Project Details", async ({ page }) => {
    await openFirstProject(page);
    await sidebarNavItem(page, "Overview").click();

    await test.step("[Positive] Project Details card shows core metadata fields", async () => {
      await expect(page.getByRole("heading", { name: "Project Details" })).toBeVisible();
      await expect(
        page.getByText("Core configuration and metadata for this project"),
      ).toBeVisible();
      await expect(page.getByText("Name", { exact: true })).toBeVisible();
      await expect(page.getByText("X-Blocks-Key", { exact: true })).toBeVisible();
      await expect(page.getByRole("main").getByText("Environment", { exact: true })).toBeVisible();
      await expect(page.getByText("Last updated Date", { exact: true })).toBeVisible();
      await expect(page.getByText("Created Date", { exact: true })).toBeVisible();
    });

    await test.step("[Security] the X-Blocks-Key value is masked, not shown in full", async () => {
      // MaskedText shows only a few leading/trailing characters (e.g. "Dad**...**926").
      const keyRow = page.getByText("X-Blocks-Key", { exact: true }).locator("..");
      await expect(keyRow).toContainText("*");
    });

    await test.step("[Positive] X-Blocks-Key value can be copied to clipboard", async () => {
      const keyRow = page.getByText("X-Blocks-Key", { exact: true }).locator("..");
      const copyButton = keyRow.getByRole("button");
      const copyTooltip = keyRow.locator("span").filter({ hasText: /^(Copy|Copied!)$/ });

      await expect(copyButton).toBeVisible();
      await copyButton.click();
      await expect(copyTooltip).toHaveText("Copied!");
    });

    await test.step("[Positive] Core APIs section lists endpoint groups with counts", async () => {
      await expect(page.getByRole("heading", { name: "Core APIs" })).toBeVisible();
      await expect(page.getByText("Available endpoints for this module")).toBeVisible();
      await expect(page.getByText(/^\d+ Endpoints$/)).toBeVisible();
      await expect(page.getByText("Auth", { exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: "Build" })).toHaveAttribute(
        "aria-expanded",
        "false",
      );
    });

    await test.step("[Positive] sidebar shows PROJECT and ENVIRONMENT context", async () => {
      await expect(page.getByText(/^Project$/i)).toBeVisible();

      await expect(page.getByRole("button", { name: /Environment/i })).toBeVisible();
    });
  });

  test("Project Overview - navigation to Deployment", async ({ page }) => {
    await openFirstProject(page);
    await sidebarNavItem(page, "Overview").click();
    await expect(page.getByRole("heading", { name: "Project Details" })).toBeVisible();

    await test.step("[Positive] Back to console returns to the project list", async () => {
      await page.getByRole("button", { name: "Back to console" }).click();
      await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible();
    });
  });
});
