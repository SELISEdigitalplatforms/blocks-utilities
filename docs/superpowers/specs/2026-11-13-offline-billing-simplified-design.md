# Offline billing, simplified

Issue: SELISEdigitalplatforms/blocks-utilities#792. Replaces the offline product shipped under spec 006 (#776–#781, #790).

## Intent

Let an operator put a customer on a paid plan **without charging a card**, because the customer pays by bank transfer. Everything else is the ordinary subscription: same plan, price, tax, discount, proration, renewal dates, invoice and invoice email. The only differences are that no card is ever charged, the invoice is born **Unpaid**, and the integrating application (Klax) records the payment later through the API, which makes it **Paid**.

The client does not want a second billing system. Spec 006 built one (three pricing modes, service statements, a dedicated console page, refused credit purchases). All of that goes.

## Goals

1. One option skips the card. All amounts match what a card customer gets for the same plan, including a plan whose amount is 0.
2. Every event that invoices a card customer today (activation, renewal, plan change, seat change, usage) invoices an offline customer too, as Unpaid, and no charge is attempted.
3. The invoice email is sent as for a card customer, on activation and on every renewal, and shows Unpaid.
4. Renewal never depends on payment. An open invoice never moves the subscription to PastDue, Unpaid or canceled.
5. API: list invoices filtered by payment status, with organization, plan, seats, period and amount; record a payment against an invoice, which moves it to Paid.
6. Tests cover activation, two consecutive renewals with an unpaid invoice, a seat change, the emails, and the list and record-payment calls.

## Non-goals

- Dunning, reminders, due dates, partial payments, overdue handling. An Unpaid invoice stays Unpaid until a payment is recorded; nothing else reacts to it.
- Switching a subscription between card and offline after activation.
- Migrating existing Contract or No-price subscriptions. Only test data exists; it is cancelled.

## Anti-goals

- Any way for an organization to skip the card for itself without the operator's say-so. Only a caller whose role carries the new permission may, and only the operator grants that permission in IAM.
- Reintroducing a separate request type, pricing mode or document type for offline customers.

## Constraints

- Permissions in this codebase are Blocks OS IAM endpoint permissions checked only by `[ProtectedEndPoint]` on a route; no controller or domain service reads a claim itself (`server/Api/Controllers/README.md`). The gate is therefore a route, not a request field.
- Klax is itself a customer: it calls Utilities with a token for its **own** organization and subscribes, lists and pays for that organization only. The existing organization rule (a caller's named `organizationId` is honoured only for the console organization; everyone else lands on their own) stays as is. The console-only check (`CallerIsConsole`) cannot be the gate because Klax is not the console.
- `FinancialDocumentStatus` (`Issued / PartiallyRefunded / Refunded`) is a refund state. Payment state is orthogonal: a Paid invoice can be refunded.
- Spec 006 once shipped a dedicated permission (`subscription-offline::manage`) and it was pulled because no role carried it (commit `0513c004`). The permission must be created in IAM and granted **before** release this time.

## Approaches considered

**A. One endpoint, one flag.** `POST /api/subscriptions` takes `collectionMethod`; the permission is checked inside the action. Literal reading of "one option", but the first endpoint in the codebase to read a permission itself, and harder to audit than a route-level grant. Rejected.

**B. Keep `POST /api/subscriptions/offline`, stripped to the ordinary request, behind its own permission. (Chosen.)** Same body and response as `POST /api/subscriptions`; the route is the option. Follows the permission convention. The route already exists.

**C. Per-plan default with per-subscription override.** The issue is explicit about per subscription. Rejected.

Decided in conversation on 2026-11-13.

## Design

### 1. Activation: two routes, one request, one service

| Route | Permission | Last step |
|---|---|---|
| `POST /api/subscriptions` | `blocks-utilities::subscription::manage` | Opens checkout, returns `checkoutUrl`; subscription waits for the card. Unchanged. |
| `POST /api/subscriptions/offline` | `blocks-utilities::subscription::manage-offline` (new) | Activates at once; `checkoutUrl` is null; `collectionMethod = OfflineInvoice`. |

- Both take `CreateSubscriptionRequest` unchanged and return `SubscriptionResponse`. `ActivateOfflineSubscriptionRequest`, `OfflinePricingRequest` and `OfflinePricingMode` are deleted.
- Both call the same `SubscribeAsync` with one boolean, `offline`. Offline skips the checkout session and sets the collection method; nothing else differs. Price snapshot, schedule, proration and discount are computed exactly as for a card customer.
- `SubscriptionCollectionMethod` stays as is (`AutomaticCharge`, `OfflineInvoice`), per subscription, set at activation only.
- The service's console-only refusal (`subscription_offline_console_only`) is removed from activation. The permission is the only gate.
- Permission name follows `manage-invoice` / `manage-capture`. Register it in `server/Api/Controllers/README.md`.

### 2. Lifecycle: the ordinary path, with the charge skipped

- Renewal, plan change, quantity change and usage rating keep their existing `IsOffline` branches that skip the gateway and record the outcome without a charge. Those already exist; they are kept, not rebuilt.
- Every branch that **refuses** an action for an offline subscription is removed (the credit-purchase refusal stays, see below): the operator-only plan change and re-price routes (`PUT /offline/plan`, `PUT /offline/pricing`), and the "console can withdraw a change it scheduled" rule from #790. An offline customer's own admins change plan and quantities through the ordinary routes, like anyone else (decided 2026-11-13): the change issues a bigger Unpaid invoice and no card is involved. They can only ever make themselves owe more, never get more for free.
- Credit purchases by an offline customer: the builder checks whether a card-less purchase can be invoiced Unpaid through the same path. If it cannot be done within this change, credit purchase for offline customers stays refused and the design doc is updated to say so. (Anti-goal remains: no new offline-specific product.)
  - **Decision: credit purchase stays refused for offline customers.** A credit purchase is a gateway charge with a settlement floor; invoicing credits Unpaid would be a new product path, which the anti-goal rules out.
- Renewal never consults payment state. There is no dunning for offline subscriptions; renewal of an offline subscription always succeeds on schedule.

### 3. Invoices: a payment state

- New field on `SubscriptionFinancialDocument`: `PaymentStatus` with values `Paid`, `Unpaid`. Plus `PaidAtUtc`, `PaymentReference` (free text, optional), `PaymentRecordedByUserId`.
- Issued **Paid** when the document is backed by a gateway payment (`PaymentDetailId` set) or its amount is zero. Issued **Unpaid** otherwise, which today means an offline subscription's invoice. Credit notes are always Paid (they are not collectable).
- Documents already in the database get `Paid` (they were all card-backed or statements; statements are deleted with their type).
- `FinancialDocumentType.ServiceStatement` and the `STM` number sequence are deleted. Offline subscriptions issue `Invoice` only.
- "Payable by bank transfer" (`FinancialDocumentHtmlTemplate.cs:818`) is replaced by the payment status, shown on the PDF, the invoice email and the console invoice list: **Unpaid** or **Paid**. Bank details still come from the merchant profile's payment instructions, as today.

### 4. API

- `GET /api/subscriptions/invoices` gains `paymentStatus` as a filter (`Paid` | `Unpaid`). Each row already carries plan, period and amounts; add `organizationId`, `paymentStatus`, `paidAtUtc` and the subscription's quantities (seats) to the row. Permission unchanged (`read-invoice`).
- `POST /api/subscriptions/invoices/{documentId}/payment` records a payment: body `{ paidAtUtc?, reference? }` (defaults to now). Moves `Unpaid → Paid`. Idempotent: a second call on a Paid invoice returns 200 with the document unchanged. Refused for credit notes. Permission: `blocks-utilities::subscription::manage-invoice` (exists; used by resend). Audited.
- The org scope of both calls is the existing resolver: Klax's token acts on Klax's own organization, the console names the organization it works on. Nothing new.

### 5. Console

- The Offline billing page, its route, menu entry, hook, pricing fields and tests are deleted.
- The Simulation page's subscribe dialog gets one checkbox, **"Payment is collected outside Utilities (no card)"**. Ticked, the page posts to `/offline` and shows the subscription active with the **Billed by invoice** badge; unticked, it behaves as today. The checkbox is always shown (my call: the client has no "which permissions do I hold" mechanism, and the page is already console-only); a user without `manage-offline` gets the server's 403, shown as the usual error toast.
- The current-subscription card keeps the badge and drops the offline pricing summaries. Quantities show as for any subscription (contract mode is gone).
- The Invoices page shows the payment status and filters by it, and offers **Record payment** on an Unpaid invoice (reference field optional).

### 6. Deletion list

Server: `OfflinePricing`, `OfflinePricingMode`, `OfflineSubscriptionTerms`, `ActivateOfflineSubscriptionRequest`, `OfflineSubscriptionChangeRequests`, `ServiceStatement` type and `STM` sequence, `PUT /offline/plan`, `PUT /offline/pricing`, `PendingPlanChange.ScheduledByOperator`, the console-only checks, the `OfflinePricingMode` column of the subscription revenue report (the `CollectionMethod` filter stays), and their tests. Client: the files listed under "Offline" in `client/app` that exist only for the page and the pricing modes, `offline-billing.spec.ts` in e2e. Docs: spec 006 README gets a note pointing here.

### 7. Rollout

1. Create `blocks-utilities::subscription::manage-offline` in Blocks OS IAM (type Endpoint, group blocks-utilities) and grant it to Klax's integration role and the console admin role. Klax's role also needs `subscription::read-invoice` and `subscription::manage-invoice`, or the list and record-payment calls 403. Without this step the feature is unreachable, as happened with spec 006.
2. Cancel the Contract-mode test subscriptions on dev (their price snapshots hold the contract amount and cannot be reinterpreted).
3. The mail module's `subscription_invoice` template must print `{{PaymentStatus}}`; until it does the email body says nothing about payment.
4. Deploy.

## Left to the builder

- Exact field names and the migration/backfill mechanism for `PaymentStatus` (Mongo documents; follow how other added fields were backfilled).
- Where the `offline` boolean enters `SubscribeAsync` and how checkout creation is skipped, keeping the zero-amount card-setup path intact.
- Whether credit purchases for offline customers can ride the Unpaid invoice path (section 2). Answered: no, they stay refused (see section 2).
- Row shape for seats in the invoice list.
- Email and PDF wording for Unpaid/Paid; keep the existing templates' tone.
