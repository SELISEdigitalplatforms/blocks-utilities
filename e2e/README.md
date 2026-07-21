# Blocks Utilities — End-to-End Tests (Playwright)

E2E tests that drive the real app through the browser, including the dev-iam
login redirect flow.

## One-time setup

1. **Configure env** — copy the template and fill in your values:
   ```bash
   cd e2e
   cp .env.e2e.example .env.e2e
   ```
   Set `E2E_BASE_URL`, `E2E_USERNAME`, `E2E_PASSWORD`. `.env.e2e` is gitignored —
   never commit real credentials.

2. **Install** Playwright + the browser:
   ```bash
   cd e2e
   npm install
   npx playwright install chromium
   ```

## Run

From the repo root:

```bash
./run.sh -te      # or: run.ps1 -te
```

or from `e2e/`:

```bash
npm test
```

Headless is the default; the post-test pause only engages with `--headed`.

### Target modes

**Remote dev** — drives the deployed host directly. No hosts entry, no certs, no
port conflict:

```ini
E2E_BASE_URL=https://dev-utilities.blocksdevelopers.com
E2E_NO_WEBSERVER=1
```

`E2E_NO_WEBSERVER=1` is required here, otherwise Playwright runs `bash run.sh -b`
to boot a local API first. You will see this warning on every remote run — it is
correct behaviour, not a fault:

```
[e2e] index.html not found at ...server/Api/wwwroot/index.html — skipping BLOCKS_UTILITIES_BASE_URL patch.
```

`global-setup.ts` only repoints a locally built SPA; against remote dev there is
nothing to patch.

> Remote dev is shared infrastructure. The login spec is effectively read-only,
> but anything that mutates data acts on **real dev records**.

**Local build on `:5000`** — omit `E2E_NO_WEBSERVER` and point at the local port:

```ini
E2E_BASE_URL=http://dev-utilities.blocksdevelopers.com:5000
```

Three preconditions cause nearly all failures here:

1. **Hosts entry** — `nslookup dev-utilities.blocksdevelopers.com` must return
   `127.0.0.1`. Watch for a commented-out `#127.0.0.1 ...` line that looks
   present but is not.
2. **Scheme must be `http://`** — unlike some sibling repos, this repo's `run.sh`
   has **no TLS handling**: it does not read any `*_SSL_CERT` / `*_SSL_KEY` env
   vars and always serves plain HTTP on `$API_PORT`. Pointing `E2E_BASE_URL` at
   `https://` will hang until timeout.
3. **Port 5000 is free** — every Blocks repo uses it; only one can run at a time.

Auto-start uses `bash run.sh -b`, so **Git Bash's `bash` must be on PATH**.

### Other run modes
```bash
npm run test:headed   # watch it in a real browser
npm run test:ui       # Playwright UI mode
npm run report        # open the last HTML report
```

## Knobs in `.env.e2e`

| Variable | Effect |
|---|---|
| `E2E_BASE_URL` | Host under test. No default — a missing value fails loudly. |
| `E2E_USERNAME` / `E2E_PASSWORD` | Dev-IAM test account. |
| `E2E_NO_WEBSERVER=1` | Don't auto-start the app; you manage the server. |
| `E2E_PAUSE_MS` | How long the browser holds after **each** test. Defaults to 10 s headed, 0 headless; `0` disables. |
| `E2E_SLOWMO` | Milliseconds of delay per action, to watch the steps themselves. |
| `E2E_HOLD_MS` | Extra hold at the end of the login spec specifically. |

## Discovering / updating selectors

The username/password fields live on the dev-iam page. To capture or verify
selectors against the live page:

```bash
npm run codegen -- <E2E_BASE_URL>/login
```

## Layout

```
e2e/
  tests/auth/login.spec.ts   # login through dev-iam -> /app/console
  support/test-base.ts       # shared test/expect with the headed pause
  fixtures/                  # auth storage state (gitignored — live token)
  playwright.config.ts       # baseURL + creds from .env.e2e
  global-setup.ts            # repoints BLOCKS_UTILITIES_BASE_URL for local builds
```
