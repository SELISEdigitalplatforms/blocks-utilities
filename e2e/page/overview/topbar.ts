import { expect, Page } from "@playwright/test";
import { e2eBaseUrl } from "../../support/env";
import { notificationRows, stubNotificationFeed } from "../../support/notification-stubs";

/**
 * Topbar flows. Each function is a reusable step that exercises a single
 * topbar behavior end-to-end (the click, the navigation, the verification).
 */

/** Land on the console with notifications stubbed — shared setup for all flows. */
export async function openConsoleWithNotifications(page: Page): Promise<void> {
  await stubNotificationFeed(page);
  await page.goto(`${e2eBaseUrl()}/app/console`, { waitUntil: "domcontentloaded" });
}

/** Topbar: switching theme to Dark applies it, then Light restores it. */
export async function switchTheme(page: Page): Promise<void> {
  const themeButton = page.getByRole("button", { name: "Change theme" });

  // Theme picker is now a popover menu (Auto/Light/Dark) that closes after
  // each selection — reopen via the trigger button between picks.
  await expect(themeButton).toBeVisible({ timeout: 30_000 });
  await themeButton.click();
  await page.getByText("Light", { exact: true }).click();
  await expect(page.locator("html")).not.toHaveClass(/dark/);

  await themeButton.click();
  await page.getByText("Dark", { exact: true }).click();
  await expect(page.locator("html")).toHaveClass(/dark/);
}

/** Topbar: language selector lists EN/German/French with non-English disabled. */
export async function verifyLanguageSelector(page: Page): Promise<void> {
  const languageButton = page.getByRole("button", { name: /^en$/i });
  await languageButton.click();
  await expect(page.getByRole("menuitem", { name: "English" })).toBeVisible();
  await expect(page.getByRole("menuitem", { name: "German" })).toHaveAttribute(
    "aria-disabled",
    "true",
  );
  await expect(page.getByRole("menuitem", { name: "French" })).toHaveAttribute(
    "aria-disabled",
    "true",
  );
  await page.keyboard.press("Escape");
  await expect(page.getByRole("menuitem", { name: "English" })).toHaveCount(0);
}

/** Topbar: an unread notification is marked read on hover (not requiring a click). */
export async function verifyNotificationMarksReadOnHover(page: Page): Promise<void> {
  const bell = page.getByTestId("notification-bell");
  const feedLoaded = page.waitForResponse(
    (response) => response.url().includes("GetNotifications") && response.ok(),
  );
  await bell.click();
  await feedLoaded;
  await expect(page.getByRole("button", { name: "Mark all as read" })).toBeVisible({
    timeout: 10_000,
  });

  const rows = notificationRows(page);
  await expect(rows).toHaveCount(1, { timeout: 10_000 });
  await expect(rows).toHaveClass(/bg-muted\/60/);

  const markedRead = page.waitForResponse(
    (response) => response.url().includes("MarkNotificationAsRead") && response.ok(),
  );
  await rows.hover({ force: true, timeout: 10_000 });
  await markedRead;
  await expect(rows).not.toHaveClass(/bg-muted\/60/, { timeout: 10_000 });

  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Mark all as read" })).toHaveCount(0);
}

/** Topbar: notification bell opens the popover and 'Mark all as read' is usable. */
export async function verifyNotificationBellPopover(page: Page): Promise<void> {
  const bell = page.getByTestId("notification-bell");
  await bell.click();
  await expect(page.getByText("Notifications", { exact: true })).toBeVisible();
  const markAllRead = page.getByRole("button", { name: "Mark all as read" });
  await expect(markAllRead).toBeVisible({ timeout: 10_000 });
  // This list re-renders live (real-time notifications), which trips
  // Playwright's actionability "stable element" wait indefinitely.
  // Force the click since the button itself is genuinely clickable.
  await markAllRead.click({ force: true, timeout: 10_000 });
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Mark all as read" })).toHaveCount(0);
}

/** Topbar: app switcher opens the SELISE Blocks apps list. */
export async function verifyAppSwitcher(page: Page): Promise<void> {
  const appsButton = page.getByRole("button", { name: "SELISE Blocks apps" });
  await appsButton.click();
  await expect(page.getByText("SELISE Blocks", { exact: true })).toBeVisible();
  // Outside-click on the top-left of the viewport closes the apps
  // popover. Re-clicking the trigger can be intercepted by the menu
  // portal, and the menu covers most of the central page area.
  await page.mouse.click(20, 20);
  await expect(page.getByText("SELISE Blocks", { exact: true })).toHaveCount(0);
}

/**
 * Topbar: user avatar menu lists 'My Profile' and 'Log out', and Profile navigates.
 * Leaves the page back on the projects list, ready for the next flow.
 */
export async function verifyUserAvatarMenu(page: Page): Promise<void> {
  const avatarTrigger = page.getByRole("button", { name: "Open user menu" });
  await expect(avatarTrigger).toBeVisible({ timeout: 10_000 });

  await avatarTrigger.click();
  const profileItem = page.getByRole("menuitem", { name: "My Profile" });
  await expect(profileItem).toBeVisible({ timeout: 10_000 });
  await expect(page.getByRole("menuitem", { name: "Log out" })).toBeVisible();

  await profileItem.click();
  await expect(page).toHaveURL(/\/profile/, { timeout: 15_000 });

  await page.goBack();
  await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
    timeout: 30_000,
  });
}
