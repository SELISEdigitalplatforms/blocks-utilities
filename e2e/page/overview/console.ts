import { expect, Page } from "@playwright/test";
import { e2eBaseUrl } from "../../support/env";
import { stubNotificationFeed } from "../../support/notification-stubs";
import { readUtilitiesProject } from "../../support/utilities-project";

/**
 * Console flows (project list, cross-app settings link, resource cards).
 * Each function is a reusable step that exercises a single console behavior.
 */

/** Land on the console with notifications stubbed — shared setup for all flows. */
export async function openConsoleWithNotifications(page: Page): Promise<void> {
  await stubNotificationFeed(page);
  await page.goto(`${e2eBaseUrl()}/app/console`, { waitUntil: "domcontentloaded" });
}

/** Console: project list shows a real project card with name and environment chip. */
export async function verifyProjectList(page: Page): Promise<void> {
  await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
    timeout: 30_000,
  });
  const fixture = readUtilitiesProject();
  if (fixture) {
    await expect(page.getByText(fixture.projectName, { exact: true }).first()).toBeVisible({
      timeout: 30_000,
    });
  }
  await expect(
    page
      .getByRole("button", {
        name: /^(Development|Production|Testing|Staging|IAT|UAT|Prod Shadow|Pre-Prod)$/,
      })
      .first(),
  ).toBeVisible({ timeout: 30_000 });
}

/**
 * Console: project card's settings icon navigates cross-app to the
 * environments overview (Blocks OS). Leaves the page back on the console.
 */
export async function verifyProjectSettingsCrossAppNavigation(page: Page): Promise<void> {
  // ProjectCard renders a Button with a Settings2 lucide icon and a
  // tooltip "Configure Project". Scope to <main> so we don't pick up
  // unrelated settings icons (e.g. sidebars, topbar). We then hover to
  // confirm the tooltip text matches -- this pins the selector to a
  // behavior, so a future lucide-react rename doesn't silently pass.
  const main = page.getByRole("main");
  const configureButton = main.locator("button:has(svg.lucide-settings-2)").first();
  await expect(configureButton).toBeVisible({ timeout: 15_000 });

  await configureButton.hover();
  await expect(page.getByRole("tooltip", { name: "Configure Project" })).toBeVisible({
    timeout: 10_000,
  });

  const consoleUrl = page.url();
  await configureButton.click();
  await expect(page).toHaveURL(/\/environments/, { timeout: 30_000 });

  await page.goto(consoleUrl, { waitUntil: "domcontentloaded" });
  await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
    timeout: 30_000,
  });
}

/**
 * Console: Resources cards (Docs/Code/Cloud) actually navigate to their
 * target URL when clicked. Each link is opened in a popup and verified
 * against its href attribute.
 */
export async function verifyResourceCardsNavigate(page: Page): Promise<void> {
  const docsLink = page.getByRole("link", { name: "Docs", exact: false });
  const codeLink = page.getByRole("link", { name: "Code", exact: false });
  const cloudLink = page.getByRole("link", { name: "Cloud", exact: false });

  for (const link of [docsLink, codeLink, cloudLink]) {
    await expect(link).toBeVisible({ timeout: 15_000 });
    await expect(link).toHaveAttribute("href", /^https?:\/\//);
    await expect(link).toHaveAttribute("target", "_blank");

    const expectedHref = await link.getAttribute("href");
    const [popup] = await Promise.all([
      page.context().waitForEvent("page", { timeout: 15_000 }),
      link.click(),
    ]);
    await popup.waitForLoadState("domcontentloaded", { timeout: 15_000 }).catch(() => {});
    const stripTrailingSlash = (url: string) => url.replace(/\/$/, "");
    expect(stripTrailingSlash(popup.url())).toBe(stripTrailingSlash(expectedHref ?? ""));
    await popup.close();
  }
}
