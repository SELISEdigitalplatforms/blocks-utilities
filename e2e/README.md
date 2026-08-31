# Blocks Utilities — End-to-End Tests (Playwright)

Follows the shared Blocks product e2e template
([`e2e-spec/SPEC-blocks-e2e-suite-template.md`](/home/noor/Office-Projects/e2e-spec/SPEC-blocks-e2e-suite-template.md)),
same shape as `blocks-data/e2e` and `blocks-logic/e2e`.

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
npm test              # utilities-setup + feature specs + utilities-teardown
npm run test:features # ordered subset from features.mjs
```

### Against remote dev (default)

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

```
E2E_BASE_URL=https://dev-utilities.blocksdevelopers.com:5000
# E2E_NO_WEBSERVER left unset / not 1
```

Hosts entry:

```
127.0.0.1 dev-utilities.blocksdevelopers.com
```

### Other run modes

```bash
npm run test:headed
npm run test:ui
npm run report
E2E_FEATURES=overview npm run test:features
```

## Knobs in `.env.e2e`

| Variable | Effect |
|---|---|
| `E2E_BASE_URL` | Blocks **Utilities** host. Dev: `https://dev-utilities.blocksdevelopers.com`. Prod: `https://utilities.seliseblocks.com`. |
| `E2E_OS_BASE_URL` | Blocks **OS** (optional). Derived: `dev-utilities`→`dev-os`, `utilities.`→`os.`. |
| `E2E_USERNAME` / `E2E_PASSWORD` | OIDC test account. |
| `PROJECT_NAME` | Optional create prefix (`${PROJECT_NAME} ${Date.now()}`). |
| `E2E_REUSE_PROJECT_NAME` | Reuse named project instead of creating. |
| `E2E_PROJECT_ID` | Open project by UUID — skips console card search. |
| `E2E_KEEP_PROJECT=1` | Never delete shared project after run. |
| `E2E_NO_WEBSERVER=1` | Don't auto-start the app (required for remote host). |
| `E2E_FEATURES` | Comma-separated feature ids or `all` for `test:features`. |
| `E2E_PAUSE_MS` | Hold browser after each test (headed debugging). |
| `E2E_SLOWMO` | Slow motion ms per Playwright action. |

## Lifecycle

Playwright projects: **`utilities-setup` → `utilities` → `utilities-teardown`**

1. **Suite setup** (`tests/suite/suite.setup.spec.ts`) — OIDC login, reuse or create one shared project, write `utilities-project.json`, then save `utilities-session.json` **after** the dashboard is open (so localStorage keeps project/env).
2. **Features** (`tests/01-overview`, `02-payments`, …) — use session; open shared dashboard with a direct `goto` to `/app/{itemId}/dashboard`.
3. **Session / context recovery** — login gate or console bounce → re-auth if needed, one env-chip open to reseed localStorage, persist session (never create a new project).
4. **Suite teardown** (`tests/suite/suite.teardown.spec.ts`) — delete on **Blocks OS** only when every `utilities` test passed (unless `E2E_KEEP_PROJECT=1`).

## Layout

```
e2e/
  features.mjs / run-e2e.mjs
  tests/
    auth/login.spec.ts            # standalone auth smoke (project "setup")
    suite/
      suite.setup.spec.ts         # login + shared project
      suite.teardown.spec.ts      # OS delete when suite passed
    overview/                     # feature specs only
    payments/
    subscription/
    magic-url/
  support/
    env.ts                        # Utilities URL + OS derivation
    login-helper.ts
    create-and-delete-project.ts
    utilities-project.ts          # utilities-session / utilities-project fixtures
    suite-helpers.ts              # openSharedProjectDashboard
    run-outcome.ts                # markSuiteTestFailed
    test-base.ts                  # pause + mark failures for project "utilities"
    utilities-helpers.ts          # feature navigation helpers
  fixtures/                       # gitignored
  SPEC-multi-env.md
```
