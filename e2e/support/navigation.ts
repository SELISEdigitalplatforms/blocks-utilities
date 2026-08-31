import { expect, Page } from "@playwright/test";
import { e2eBaseUrl } from "./env";
import { openSharedProjectDashboard } from "./suite-helpers";

/**
 * Land on the shared suite project's dashboard (Project Details).
 *
 * Prefers the data-setup fixture. Falls back to clicking an environment chip
 * on the console when no fixture exists (e.g. isolated debugging).
 */
export async function openEnvironment(
  page: Page,
  name: string | RegExp = /Development/,
): Promise<void> {
  try {
    await openSharedProjectDashboard(page);
    return;
  } catch {
    // Fall through to console chip click.
  }

  await page.goto(`${e2eBaseUrl()}/app/console`, { waitUntil: "domcontentloaded" });
  const envButton = page.getByRole("button", { name }).first();
  const detailsHeading = page.getByRole("heading", { name: "Project Details" });

  let reached = false;
  for (let attempt = 0; attempt < 3 && !reached; attempt++) {
    await envButton.click();
    reached = await detailsHeading
      .waitFor({ state: "visible", timeout: 10_000 })
      .then(() => true)
      .catch(() => false);
  }

  await expect(detailsHeading).toBeVisible({ timeout: 10_000 });
}
