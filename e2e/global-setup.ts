import fs from "fs";
import path from "path";

/**
 * Point the locally-served Blocks Utilities at itself (:5000), not the remote dev host.
 *
 * The built index.html carries runtime config in `window.__BLOCKS_ENV__`. The
 * .NET host bakes `BLOCKS_UTILITIES_BASE_URL` from the Mongo secret, which is the
 * deployed host WITHOUT a port (https://dev-utilities.blocksdevelopers.com).
 * When we run Utilities locally on :5000, the SPA would then send its API calls to
 * the remote dev server, so the console shows no local data. This patches the
 * served index.html so BLOCKS_UTILITIES_BASE_URL === E2E_BASE_URL.
 *
 * Idempotent and order-independent: it rewrites the concrete value (or the
 * `__BLOCKS_UTILITIES_BASE_URL__` placeholder), so it holds whether it runs before or
 * after the host's own startup replacement. Because the command in
 * playwright.config.ts is `run.sh -b` (no FE rebuild), nothing overwrites it.
 */
export default function globalSetup() {
  const baseURL = process.env.E2E_BASE_URL;
  if (!baseURL) return; // playwright.config.ts already throws when unset

  const indexHtml = path.resolve(__dirname, "../server/Api/wwwroot/index.html");

  let original: string;
  try {
    original = fs.readFileSync(indexHtml, "utf8");
  } catch (error) {
    const err = error as NodeJS.ErrnoException;
    if (err.code === "ENOENT") {
      console.warn(
        `[e2e] index.html not found at ${indexHtml} — skipping BLOCKS_UTILITIES_BASE_URL patch. ` +
          `Build the FE first (cd client && npm run build, or run.sh -a).`,
      );
      return;
    }
    throw error;
  }

  const patched = original.replace(
    /(BLOCKS_UTILITIES_BASE_URL:\s*")([^"]*)(")/g,
    `$1${baseURL}$3`,
  );

  if (patched === original) {
    console.log(`[e2e] BLOCKS_UTILITIES_BASE_URL already "${baseURL}" — no patch needed.`);
    return;
  }

  const tmpPath = path.join(
    path.dirname(indexHtml),
    `.index.html.e2e-patch.${process.pid}.tmp`,
  );
  try {
    fs.writeFileSync(tmpPath, patched, "utf8");
    fs.renameSync(tmpPath, indexHtml);
  } catch (error) {
    try {
      fs.unlinkSync(tmpPath);
    } catch {
      // Best-effort cleanup of the temp file.
    }
    throw error;
  }

  console.log(`[e2e] Patched BLOCKS_UTILITIES_BASE_URL -> "${baseURL}" in served index.html.`);
}
