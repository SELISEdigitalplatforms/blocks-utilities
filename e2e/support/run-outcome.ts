import fs from "fs"
import path from "path"

const OUTCOME_PATH = path.resolve(__dirname, "../fixtures/run-outcome.json")

let suiteTestsFailed = false

export function markSuiteTestFailed() {
  suiteTestsFailed = true
  fs.mkdirSync(path.dirname(OUTCOME_PATH), { recursive: true })
  fs.writeFileSync(OUTCOME_PATH, JSON.stringify({ failed: true }))
}

/** Delete shared project only when every suite test passed. */
export function shouldDeleteSharedProject(): boolean {
  if (process.env.E2E_KEEP_PROJECT === "1") return false
  if (fs.existsSync(OUTCOME_PATH)) return false
  return !suiteTestsFailed
}

export function resetRunOutcome() {
  suiteTestsFailed = false
  if (fs.existsSync(OUTCOME_PATH)) fs.unlinkSync(OUTCOME_PATH)
}
