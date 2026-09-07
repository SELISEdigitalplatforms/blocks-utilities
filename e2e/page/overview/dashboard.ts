import { expect, Locator, Page } from "@playwright/test";
import { e2eBaseUrl } from "../../support/env";
import { stubNotificationFeed } from "../../support/notification-stubs";
import { readUtilitiesProject } from "../../support/utilities-project";
import { openEnvironment } from "../../support/navigation";

/**
 * Environment / dashboard flows (Workspace sidebar, Project Details card,
 * Core APIs card). Each function is a reusable step that exercises a single
 * behavior end-to-end.
 *
 * Assumes the caller has already opened an environment via openEnvironment().
 * Use `openDashboard` to land here from a fresh page.
 */

/** Land on the project dashboard with notifications stubbed; returns the dashboard URL. */
export async function openDashboard(page: Page): Promise<string> {
  await stubNotificationFeed(page);
  await page.goto(`${e2eBaseUrl()}/app/console`, { waitUntil: "domcontentloaded" });
  await openEnvironment(page);
  await expect(page.getByRole("heading", { name: "Project Details" })).toBeVisible({
    timeout: 30_000,
  });
  return page.url();
}

/** 'Overview' sidebar link is itemId-scoped and actually navigates back to the dashboard. */
export async function verifyOverviewSidebarLink(
  page: Page,
  dashboardUrl: string,
): Promise<void> {
  await page.getByRole("button", { name: "Payments", exact: true }).click();
  await expect(page).not.toHaveURL(dashboardUrl);

  const overviewLink = page.getByRole("link", { name: "Overview" }).first();
  await expect(overviewLink).toHaveAttribute("href", /\/app\/[^/]+\/dashboard$/);

  await overviewLink.click();
  await expect(page).toHaveURL(dashboardUrl);
  await expect(page.getByRole("heading", { name: "Project Details" })).toBeVisible({
    timeout: 30_000,
  });
}

/**
 * Workspace area (sidebar): Project/Environment widgets show current context
 * and are permanently disabled.
 */
export async function verifyWorkspaceWidgets(page: Page): Promise<void> {
  // The "Workspace" label lives in the desktop sidebar's section
  // header (sidebar-menu-desktop.tsx). The sidebar is a div with no
  // landmark role, so we can't scope to complementary/navigation --
  // the Project Details card also has "Project" labels. Instead,
  // we rely on the fact that the "Workspace" paragraph is unique on
  // the page (it only appears in the sidebar) and that the disabled
  // Project/Environment buttons only render inside the sidebar.
  await expect(page.getByText("Workspace", { exact: true })).toBeVisible({
    timeout: 15_000,
  });

  const projectWidget = page.getByRole("button", { name: /^Project/ });
  const environmentWidget = page.getByRole("button", { name: /^Environment/ });
  await expect(projectWidget).toBeVisible();
  await expect(environmentWidget).toBeVisible();
  await expect(projectWidget).toBeDisabled();
  await expect(environmentWidget).toBeDisabled();

  const fixture = readUtilitiesProject();
  if (fixture) {
    await expect(projectWidget).toContainText(fixture.projectName);
  }
  const environmentText = await environmentWidget.innerText();
  expect(environmentText.toLowerCase()).toContain("environment");
  expect(environmentText.replace(/environment/i, "").trim().length).toBeGreaterThan(0);
}

/** Project Details card shows Name, X-Blocks-Key, and a human-readable Environment badge. */
export async function verifyProjectDetailsCard(page: Page): Promise<void> {
  const main = page.getByRole("main");
  await expect(main.getByText("Name", { exact: true })).toBeVisible();
  await expect(main.getByText("X-Blocks-Key", { exact: true })).toBeVisible();
  await expect(main.getByText("Environment", { exact: true })).toBeVisible();

  await expect(
    main.getByRole("button", { name: /^(Production|Development|Testing|Staging|IAT)$/ }),
  ).toBeVisible();
}

/** X-Blocks-Key is masked, and its hover-reveal copy button works. */
export async function verifyXBlocksKeyMaskAndCopy(page: Page): Promise<void> {
  const keyRow = page.getByText("X-Blocks-Key", { exact: true }).locator("..");
  await expect(keyRow).toContainText("*");

  const copyButton = keyRow.getByRole("button");
  await page.context().grantPermissions(["clipboard-read", "clipboard-write"]);
  await copyButton.hover();
  await copyButton.click();
  await expect(copyButton).toHaveAttribute("aria-label", "Copied!", { timeout: 10_000 });
}

/**
 * Expand a Core APIs endpoint group button. Retries on click races with
 * the live list re-rendering; asserts expansion at the end.
 */
export async function expandEndpointGroup(button: Locator): Promise<void> {
  for (let attempt = 0; attempt < 5; attempt++) {
    try {
      if ((await button.getAttribute("aria-expanded")) === "true") return;
      await button.scrollIntoViewIfNeeded();
      await button.click({ timeout: 10_000 });
    } catch {
      // retry — the Core APIs list re-renders and can intercept clicks
    }
  }
  await expect(button).toHaveAttribute("aria-expanded", "true", { timeout: 15_000 });
}

/** Core APIs card lists endpoint groups, collapsed by default, and expands on click. */
export async function verifyCoreApisExpansion(page: Page): Promise<void> {
  await expect(page.getByRole("heading", { name: "Core APIs" })).toBeVisible({
    timeout: 30_000,
  });
  await expect(page.getByText("Available endpoints for this module")).toBeVisible();
  await expect(page.getByText(/^\d+ Endpoints?$/)).toBeVisible();

  const groupButtons = page.getByRole("button", { name: /^[A-Za-z]+\s+\d+$/ });
  await expect(groupButtons.first()).toBeVisible({ timeout: 15_000 });
  const groupCount = await groupButtons.count();
  expect(groupCount).toBeGreaterThan(0);

  const firstGroup = groupButtons.first();
  await expect(firstGroup).toHaveAttribute("aria-expanded", "false");

  await expandEndpointGroup(firstGroup);
}

/** 'Copy as cURL' on an endpoint is hover-reveal and copies something to the clipboard. */
export async function verifyCopyAsCurl(page: Page): Promise<void> {
  const curlRow = page.getByText("Copy as cURL").first().locator("..");
  await expect(curlRow).toBeVisible({ timeout: 15_000 });
  const curlButton = curlRow.getByRole("button");

  await page.context().grantPermissions(["clipboard-read", "clipboard-write"]);
  await curlButton.scrollIntoViewIfNeeded();
  await curlButton.hover();
  await curlButton.click({ timeout: 15_000 });
  await expect(curlButton).toHaveAttribute("aria-label", "Copied!", { timeout: 10_000 });
  const clipboardText = await page.evaluate(() => navigator.clipboard.readText());
  expect(clipboardText.length).toBeGreaterThan(0);
  expect(clipboardText).toContain("curl");
}
