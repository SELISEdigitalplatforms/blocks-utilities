#!/usr/bin/env node
/**
 * Run enabled utilities features in order (one login, stop on first failure).
 * Edit features.mjs or set E2E_FEATURES=overview,create-payment
 */
import { spawnSync } from "node:child_process"
import path from "node:path"
import { fileURLToPath } from "node:url"
import { resolveEnabledFeatures } from "./features.mjs"

const __dirname = path.dirname(fileURLToPath(import.meta.url))

function main() {
  const features = resolveEnabledFeatures()

  if (features.length === 0) {
    console.error("[e2e] No features enabled. Edit features.mjs or set E2E_FEATURES.")
    process.exit(1)
  }

  console.log(`[e2e] Running ${features.length} feature(s) in order:`)
  for (const feature of features) {
    console.log(`  - ${feature.id}: ${feature.name}`)
  }

  const specs = features.map((feature) => feature.spec)
  const result = spawnSync(
    "npx",
    ["playwright", "test", ...specs, "--max-failures=1"],
    {
      cwd: __dirname,
      stdio: "inherit",
      env: process.env,
    },
  )

  process.exit(result.status ?? 1)
}

main()
