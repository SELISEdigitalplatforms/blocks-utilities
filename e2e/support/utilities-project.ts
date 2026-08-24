import fs from "fs"
import path from "path"

export type UtilitiesProjectFixture = {
  projectName: string
  itemId: string
  dashboardUrl: string
}

const FIXTURE_PATH = path.resolve(__dirname, "../fixtures/utilities-project.json")
export const UTILITIES_SESSION_PATH = path.resolve(__dirname, "../fixtures/utilities-session.json")

export function readUtilitiesProject(): UtilitiesProjectFixture | null {
  if (!fs.existsSync(FIXTURE_PATH)) return null
  return JSON.parse(fs.readFileSync(FIXTURE_PATH, "utf8")) as UtilitiesProjectFixture
}

export function writeUtilitiesProject(fixture: UtilitiesProjectFixture) {
  fs.mkdirSync(path.dirname(FIXTURE_PATH), { recursive: true })
  fs.writeFileSync(FIXTURE_PATH, JSON.stringify(fixture, null, 2))
}

export function clearUtilitiesProject() {
  if (fs.existsSync(FIXTURE_PATH)) fs.unlinkSync(FIXTURE_PATH)
}

export function clearUtilitiesSession() {
  if (fs.existsSync(UTILITIES_SESSION_PATH)) fs.unlinkSync(UTILITIES_SESSION_PATH)
}

export function utilitiesSessionExists(): boolean {
  return fs.existsSync(UTILITIES_SESSION_PATH)
}
