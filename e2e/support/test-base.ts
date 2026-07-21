import { test as base, expect } from "@playwright/test";

// Shared `test` for the whole suite. Specs import from here instead of
// "@playwright/test" so the pause below applies everywhere automatically.
//
// Headed runs hold the browser open for a moment after each test finishes, so
// the end state is actually watchable instead of vanishing the instant the
// assertions pass. Headless runs (CI, plain `npm test`) are untouched.
//
//   E2E_PAUSE_MS=0      disable
//   E2E_PAUSE_MS=30000  hold longer
//   E2E_PAUSE_MS=3000 npm test   force it on in headless too

function pauseMs(isHeaded: boolean): number {
  const configured = process.env.E2E_PAUSE_MS;

  if (configured !== undefined && configured !== "") {
    const parsed = Number(configured);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
  }

  return isHeaded ? 10_000 : 0;
}

export const test = base.extend<{ pauseAfterEachTest: void }>({
  pauseAfterEachTest: [
    async ({ page }, use, testInfo) => {
      // `--headed` flips headless to false on the resolved project config.
      const isHeaded = testInfo.project.use.headless === false;
      const ms = pauseMs(isHeaded);

      // The pause runs inside the test's time budget, so give it back.
      if (ms > 0) testInfo.setTimeout(testInfo.timeout + ms);

      await use();

      // Teardown: runs after the test body, before `page` is disposed.
      if (ms > 0 && !page.isClosed()) {
        await page.waitForTimeout(ms);
      }
    },
    { auto: true },
  ],
});

export { expect };
