# Blocks Utilities — End-to-End Tests (Playwright)

E2E tests that drive the real app through the browser, including the dev-iam
login redirect flow. Utilities tests follow the same shared Blocks E2E pattern
as `e2e-logic` (login → session storage → shared project → features → teardown).

## One-time setup

1. **Configure env**: copy the template and fill in your values:
   ```bash
   cd e2e
   cp .env.e2e.example .env.e2e
   ```
   Set `E2E_USERNAME` / `E2E_PASSWORD`. `.env.e2e` is gitignored; never commit
   real credentials.

2. **Install** Playwright + the browser:
   ```bash
   cd e2e
   npm install
   npx playwright install chromium
   ```

## Run

From the repo root:

```bash
./run.sh -te          # or: .\run.ps1 -te
```

or directly:

```bash
cd e2e
npm test              # setup + all feature specs + teardown
npm run test:features # ordered subset from features.mjs
```

### Against remote dev (default)

The shipped `.env.e2e.example` targets the deployed dev host and sets
`E2E_NO_WEBSERVER=1`, so nothing is built or started locally:

```
E2E_BASE_URL=https://dev-utilities.blocksdevelopers.com
E2E_NO_WEBSERVER=1
```

Reuse an existing project (recommended when console slots are limited):

```
E2E_REUSE_PROJECT_NAME=test
# or
E2E_PROJECT_ID=<uuid>
E2E_KEEP_PROJECT=1
```

When reusing a non-ephemeral project, set `E2E_KEEP_PROJECT=1` so teardown does
not delete it after a green run.

### Against a local build

Build the FE into `server/Api/wwwroot` and let Playwright start the API on
`API_PORT` (**5000**, see `run.sh`):

```
E2E_BASE_URL=http://dev-utilities.blocksdevelopers.com:5000
# E2E_NO_WEBSERVER left unset / not 1
```

This needs a hosts entry pointing the domain at your machine:

```
127.0.0.1 dev-utilities.blocksdevelopers.com
```

Auto-start runs `bash run.sh -b`, so **Git Bash's `bash` must be on PATH**. To
manage the server yourself, set `E2E_NO_WEBSERVER=1`.

### Other run modes

```bash
npm run test:headed   # watch it in a real browser
npm run test:ui       # Playwright UI mode
npm run report        # open the last HTML report
E2E_FEATURES=overview npm run test:features   # single feature
```

## Knobs in `.env.e2e`

| Variable | Effect |
|---|---|
| `E2E_BASE_URL` | Utilities app host under test. No default; missing value fails loudly. |
| `E2E_OS_BASE_URL` | OS host for project delete (defaults: dev-utilities → dev-os). |
| `E2E_USERNAME` / `E2E_PASSWORD` | Dev-IAM test account. |
| `E2E_REUSE_PROJECT_NAME` | Reuse named project instead of creating `Test Project *`. |
| `E2E_PROJECT_ID` | Open project by UUID — skips console card search. |
| `E2E_KEEP_PROJECT=1` | Never delete shared project after run. |
| `E2E_NO_WEBSERVER=1` | Don't auto-start the app (required for remote dev). |
| `E2E_FEATURES` | Comma-separated feature ids or `all` for `test:features`. |
| `E2E_PAUSE_MS` | Hold browser after each test (headed debugging). |
| `E2E_SLOWMO` | Slow motion ms per Playwright action. |

## Layout

```
e2e/
  features.mjs                    # ordered utilities feature list
  run-e2e.mjs                     # sequential feature runner
  tests/
    auth/login.spec.ts            # standalone auth smoke test
    utilities.setup.spec.ts       # login + shared project
    utilities.teardown.spec.ts    # OS delete when all passed
    01-overview/                  # feature specs
    02-payments/
    04-magic-url/
  support/
    env.ts                        # E2E_BASE_URL, E2E_OS_BASE_URL, credentials
    login-helper.ts               # OIDC / dev-iam flow
    test-base.ts                  # shared test + failure tracking
    run-outcome.ts                # pass/fail → delete or keep project
    utilities-project.ts          # fixture read/write
    create-and-delete-project.ts  # console nav, reuse/create, OS delete
    utilities-helpers.ts          # open dashboard / overview / payments
```

## How a run works

1. **utilities-setup** — OIDC login, save `fixtures/utilities-session.json`,
   reuse or create one shared project, write `fixtures/utilities-project.json`.
2. **utilities** — feature specs load the saved session and open pages via the
   project fixture (no re-login).
3. **utilities-teardown** — delete the shared project on Blocks OS only when
   every utilities test passed (and `E2E_KEEP_PROJECT` is not set). On failure
   the project is kept for debugging.
