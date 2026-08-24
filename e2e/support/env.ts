export function requireEnv(name: string): string {
  const value = process.env[name]
  if (!value) {
    throw new Error(`${name} is not set. Fill it in e2e/.env.e2e.`)
  }
  return value
}

export function e2eBaseUrl(): string {
  return requireEnv("E2E_BASE_URL")
}

export function e2eProjectId(): string | undefined {
  const value = process.env.E2E_PROJECT_ID?.trim()
  return value || undefined
}

/** Blocks OS — project delete only (Utilities has no project Delete UI). */
export function e2eOsBaseUrl(): string {
  const explicit = process.env.E2E_OS_BASE_URL
  if (explicit) return explicit.replace(/\/$/, "")

  const utilities = e2eBaseUrl()
  if (/dev-utilities/i.test(utilities)) {
    return utilities.replace(/dev-utilities/i, "dev-os")
  }

  throw new Error(
    "E2E_OS_BASE_URL is not set and could not be derived from E2E_BASE_URL. " +
      "Set E2E_OS_BASE_URL in e2e/.env.e2e (e.g. https://dev-os.blocksdevelopers.com).",
  )
}

export function e2eCredentials(): { email: string; password: string } {
  return {
    email: requireEnv("E2E_USERNAME"),
    password: requireEnv("E2E_PASSWORD"),
  }
}
