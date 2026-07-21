import { defineConfig, devices } from "@playwright/test";
import dotenv from "dotenv";
import path from "path";

// Load credentials + target host from the gitignored .env.e2e file.
dotenv.config({ path: path.resolve(__dirname, ".env.e2e") });

const baseURL = process.env.E2E_BASE_URL;

// No localhost fallback on purpose: the app is served on a named domain, so a
// missing value should fail loudly instead of silently hitting the wrong host.
if (!baseURL) {
  throw new Error(
    "E2E_BASE_URL is not set. Copy e2e/.env.e2e.example to e2e/.env.e2e and set E2E_BASE_URL to your named domain.",
  );
}

// Set E2E_NO_WEBSERVER=1 to skip auto-start (e.g. when you already have the app
// running yourself, or on a machine without Git Bash's `bash` on PATH).
const autoStartServer = process.env.E2E_NO_WEBSERVER !== "1";

export default defineConfig({
  testDir: "./tests",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  // Serial: these tests mutate shared backend state (create/delete real
  // projects on dev), so running them in parallel would race.
  workers: 1,
  reporter: [["html", { open: "never" }], ["list"]],
  // Playwright's 30s default is shorter than the waits the specs themselves
  // declare (login.spec.ts allows 45s for the post-OIDC callback), so the
  // per-test budget would expire before its own wait could. Against remote dev
  // that callback settles in ~35-40s, so 30s failed on timing alone.
  timeout: 120_000,
  // Patches the served index.html so BLOCKS_UTILITIES_BASE_URL points at the local
  // :5000 host (E2E_BASE_URL) instead of the remote dev server.
  globalSetup: "./global-setup.ts",
  use: {
    baseURL,
    trace: "on-first-retry",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
    ignoreHTTPSErrors: true,
    // Slow each action down so the flow is watchable in headed mode.
    // e.g. E2E_SLOWMO=600 npm run test:headed
    launchOptions: {
      slowMo: process.env.E2E_SLOWMO ? Number(process.env.E2E_SLOWMO) : 0,
    },
  },
  // One command runs everything: build the FE + start API/Worker (run.sh -a),
  // wait until baseURL responds, run the tests, then tear the server down.
  // If a server is already listening at baseURL it is reused instead.
  ...(autoStartServer
    ? {
        webServer: {
          command: "bash run.sh -b",
          cwd: path.resolve(__dirname, ".."),
          url: baseURL,
          reuseExistingServer: true,
          ignoreHTTPSErrors: true,
          timeout: 600_000,
          stdout: "pipe" as const,
          stderr: "pipe" as const,
          // Documented override (Program.cs): FrontendRuntime__BLOCKS_* env vars
          // win over the Mongo secret. Ensures a fresh build (run.sh -a) also
          // bakes the local :5000 host. No-op for -b (no placeholder left).
          env: {
            FrontendRuntime__BLOCKS_UTILITIES_BASE_URL: baseURL,
          },
        },
      }
    : {}),
  projects: [
    // Setup: performs the real login once and saves the session to
    // fixtures/auth.json (see login.spec.ts).
    {
      name: "setup",
      testMatch: /auth[\\/]login\.spec\.ts/,
      use: { ...devices["Desktop Chrome"] },
    },
    // All other tests run authenticated by reusing that saved session, and
    // only after "setup" (login) has succeeded.
    {
      name: "chromium",
      testIgnore: /auth[\\/]login\.spec\.ts/,
      dependencies: ["setup"],
      use: {
        ...devices["Desktop Chrome"],
        storageState: "fixtures/auth.json",
      },
    },
  ],
});
