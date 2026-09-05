# Blocks Utilities

Utilities service of the SELISE `<Blocks/>` platform. It bundles the cross-cutting product capabilities that the other Blocks services and consoles rely on:

- **Subscriptions and billing**: plans, prices, discounts and campaigns, usage meters and entitlements, renewals, plan changes, and financial documents
- **Payments**: hosted checkout, payment methods, payment validation, provider webhooks (Adyen, Stripe) and reconciliation
- **Template engine**: reusable server-side templates rendered on demand
- **Magic links**: single-use signed links and short URLs
- **PDF generation**: server-side PDF rendering, stamping, and conversion of word-processing documents to PDF
- **Sequences and identifiers**: ordered, unique identifier generation
- **Geolocation**: IP geolocation lookups through a third-party provider
- **Mail requests**: billing email is composed here and **published to the platform's mail service** for delivery — see [Mail](#mail) below

The repository ships a web console (React) for managing these utilities and an HTTP API plus a background worker (both .NET) that implement them.

## Repository layout

```
server/   .NET backend
  Api/                          ASP.NET Core HTTP API; also serves the built SPA from wwwroot
  Worker/                       background service: queue consumers, payment reconciliation, outbox
  Subscription.DomainService/   subscriptions, plans, discounts, billing, usage and entitlements
  Payment.DomainService/        checkout, payment methods, providers, webhooks, reconciliation
  Utility.DomainService/        magic links, PDF generation, template engine, sequences, geolocation
  XUnitTest/                    backend unit tests (xUnit)
  Blocks.slnx                   solution file
client/   web console (React 18, TypeScript, Vite, Tailwind, Radix UI; Vitest for unit tests)
e2e/      end-to-end tests (Playwright), see e2e/README.md
scripts/  scan and deploy entry points
```

The API and Worker are built on the `SeliseBlocks.Genesis.OS` platform package, which provides configuration, secrets resolution, messaging (RabbitMQ or Azure Service Bus), logging and the authentication middleware.

In production the SPA is compiled into `server/Api/wwwroot` and served by the API itself, which substitutes `__BLOCKS_*__` placeholders in `index.html` with runtime values from configuration (see `ApplyFrontendRuntimeSettings` in `server/Api/Program.cs`).

## Mail

This service does not deliver email. It composes a mail request and publishes it to the platform's
mail listener, which owns the templates, the transport and the sending:

- `Subscription.DomainService/Messaging/SendMail.cs` is the wire contract. It is deliberately a copy
  of `DomainService.Dtos.SendMail` in blocks-os rather than a shared dependency, so **property names
  and shapes here must stay compatible with that listener** — changing one side without the other
  stops mail being delivered, silently.
- `SubscriptionFinancialDocumentDeliveryService` publishes invoice and financial-document mail.
- `UsageThresholdEmailService` publishes usage-threshold warnings.
- `MailDeliveryReporter` records the outcome of each publish, including the payload, so a mail that
  never arrives can be traced from this side.

There is therefore no SMTP configuration, no mail template store and no template editor in this
repository. How a message looks, and where it is finally sent, belongs to the mail service.

## Prerequisites

- **.NET SDK 10.0** (the projects target `net10.0`; verified with SDK 10.0.302)
- **Node.js and npm** (verified with Node.js 24 and npm 12; the Docker build uses Node 22)
- For e2e: the Playwright Chromium browser (`npx playwright install chromium` inside `e2e/`)

## Setup

```bash
# backend
dotnet restore server/Api/Api.csproj
dotnet restore server/Worker/Worker.csproj

# frontend and e2e (npm ci installs exactly what the lockfile pins)
npm ci --prefix client
npm ci --prefix e2e
```

## Running locally

`run.sh` (Linux/macOS/Git Bash) and `run.ps1` (Windows PowerShell) wrap the common workflows:

```bash
./run.sh -b        # run the API (port 5000)
./run.sh -w        # run the Worker
./run.sh -f        # run the frontend dev server (port 4000)
./run.sh -a        # build the frontend into wwwroot, then run API + Worker
./run.sh -k        # free port 5000
./run.sh -h        # all options, including test shortcuts (-tf, -tb, -te, -ta)
```

**Prerequisite the scripts cannot create for you:** at startup the API and Worker resolve their secrets (database, message bus, vault material) through the Genesis configuration layer and then load additional configuration from a MongoDB `Secrets` collection. Without access to a configured secret store and database the backend will not boot. The frontend dev server runs standalone: copy `client/.env.example` to `client/.env`, fill in the values for your environment, then use `npm --prefix client run local` (plain Vite on localhost) or `./run.sh -f` (`npm run dev`, which binds to the named dev host).

Container images: `Dockerfile` builds the API image (frontend build stage + .NET publish stage), `Dockerfile.worker` builds the Worker image.

## Tests

Backend unit tests (no `.sln` at the repo root, target the test project directly):

```bash
dotnet test server/XUnitTest/XUnitTest.csproj
```

Frontend unit tests:

```bash
npm --prefix client run test
```

End-to-end tests (Playwright). These need a reachable instance of the app and a test account; configure `e2e/.env.e2e` first (copy `e2e/.env.e2e.example`). Details, target modes and troubleshooting are in [e2e/README.md](e2e/README.md):

```bash
npm --prefix e2e run test
```

Coverage, when you need numbers rather than pass/fail:

```bash
dotnet test server/XUnitTest/XUnitTest.csproj --collect:"XPlat Code Coverage"
npm --prefix client run test:coverage
```

## Scanning and deployment

- `scripts/scan.sh` is the repository's security scan entry point (SAST, SCA and secret scanning). It is intentionally not tracked in git; internal environments provide it.
- `scripts/deploy.sh` is the deployment entry point.
- `scripts/payment-key-vault/` contains the one-time setup scripts for the payment provider token encryption key ring.

CI for this repository lives in `.github/workflows/` (per-environment pipelines plus repo hygiene checks).

## Configuration

No secret values belong in this repository. The variables below are names only; set the values in your own environment.

**Backend** (`server/Api/appsettings*.json`, `server/Worker/appsettings*.json`, plus the vault and the MongoDB-backed `Secrets` document): logging levels, `GeolocationApiUrl`, `GeolocationApiKey`, `RootTenantId`, `ShortUrlBaseAddress`, `PdfToolPath`, `SnsConfigurationName`, and the `Payment` section (rate limits, lock and lease durations, webhook size and timeout budgets, currency minor units; the Worker adds outbox, webhook batch and reconciliation settings).

**Frontend** (`client/.env`, template in `client/.env.example`): `BLOCKS_API_BASE_URL`, `BLOCKS_X_BLOCKS_KEY`, `BLOCKS_GOOGLE_SITE_KEY`, `BLOCKS_CONSTRUCT_URL`, `BLOCKS_IDP_BASE_URL`, `BLOCKS_OIDC_CLIENT_ID`.

**E2E** (`e2e/.env.e2e`, template in `e2e/.env.e2e.example`): `E2E_BASE_URL`, `E2E_USERNAME`, `E2E_PASSWORD` and optional knobs documented in [e2e/README.md](e2e/README.md).

## Contributing and security

- Contribution workflow, branch model and review expectations: [CONTRIBUTING.md](CONTRIBUTING.md)
- Reporting a vulnerability: [SECURITY.md](SECURITY.md)
- Community standards: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)

## License

[MIT](LICENSE)
