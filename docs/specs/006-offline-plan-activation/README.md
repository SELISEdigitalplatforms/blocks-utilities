# Spec 006: Offline Plan Activation

**Status:** Approved (2026-11-05)
**Date:** 2026-11-05
**Tracks:** [#745](https://github.com/SELISEdigitalplatforms/blocks-utilities/issues/745)
**Builds on:** subscription lifecycle (`docs/subscription/lifecycle`), financial documents,
plan change and quantity change flows

## Problem

Some Klax customers sign a contract and pay by bank transfer against an invoice, not by card.
Utilities cannot activate a paid plan for them: every paid path ends in a stored-card charge or
a hosted checkout. Today the only no-payment activation (`StartWithoutPaymentAsync`) is reached
when the initial amount is zero, and renewal moves any subscription without a stored card
straight to `Unpaid` (`no_payment_method`) before it looks at the amount.

Utilities must not collect money from these customers. It still issues invoices and sends the
invoice mail, as a record only; payment is collected and reconciled by Klax outside Utilities.
Some contracts are priced entirely outside Utilities, with no amount Utilities could put on an
invoice.

Success: a tenant admin activates a paid plan for an organization with no card and no checkout;
the subscription renews on schedule until canceled, never attempts a charge, never enters
dunning, and every billable event produces either a record invoice marked as payable by bank
transfer or, when the contract has no price in Utilities, a service statement.

## Scope

### In

- **Offline activation** by a tenant admin, for a named organization, with no payment: plan,
  price, quantities, discount code, and one of three pricing modes (catalogue, contract, none).
- **Renewal** on schedule until canceled, with no charge attempt, no `PastDue`, no `Unpaid`.
- **Record invoices** for priced events (activation, renewal, trial conversion, plan change,
  quantity change, usage overage), marked "Payable by bank transfer".
- **Service statements**: a new financial document type for no-price subscriptions, with no
  amounts, own number sequence, mailed and listed like invoices.
- **Re-pricing** of a live offline subscription by the tenant admin, from the next renewal.
- **Plan and quantity changes** by the tenant admin or the organization's own admin.
- **Reporting**: offline subscriptions counted in MRR at their invoiced amount (no-price at 0),
  filterable by collection method.
- **Client console**: tenant-admin activation and re-pricing page; "Billed by invoice" badge on
  the customer's subscription page with card prompts hidden.
- **Simulation harness**: create offline subscriptions and run their renewals.
- **E2E**: activate, renew, receive invoice or statement.

### Out

- Customer self-serve choice of offline billing.
- Tracking whether an offline invoice was paid: no mark-paid, no reconciliation, no reminders.
- Due dates on offline invoices.
- Contract end dates; the subscription renews until canceled.
- Activation on a future or past date; the first period starts at activation.
- Switching an existing subscription between offline and card.
- Per-contract tax terms; tax mode and rate always come from the plan price.
- Credit purchases for offline customers: spec 002 top-ups on an offline subscription, and
  spec 005 organization credit purchases while the organization holds a live offline
  subscription, are refused. Admin free grants (spec 005) are unaffected.

## Domain Model

### `SubscriptionDetail` (extended)

| Field | Notes |
|---|---|
| `CollectionMethod` | new enum, persisted with explicit values: `AutomaticCharge = 0` (every existing document), `OfflineInvoice = 1`. Set at creation, never changed. |
| `OfflinePricing` | null for `AutomaticCharge`. `{ Mode, ContractAmountMinor?, ContractCurrencyCode? }` where `Mode` is `Catalogue = 0`, `Contract = 1`, `None = 2`. |
| `PendingOfflinePricing` | same shape, set by re-pricing, applied and cleared at the next renewal. |
| `ActivatedBy` | actor snapshot of the tenant admin who activated it (as `PurchasedBy`). |

Rules:

- **Contract** price is one flat amount and currency per billing interval of the chosen plan
  price, whatever the quantities, frozen on the subscription. Catalogue price edits never touch
  it. Tax mode and rate come from the plan price. It is net: the price's automatic discount and
  the plan's volume tiers do not apply to it; a discount code the admin applies does. A quantity
  change does not change the amount and issues no document. Usage overage uses the plan's meter
  rates. _(Clarified 2026-11-05 during implementation.)_
- **Catalogue** pricing behaves exactly as a card subscription's frozen `PriceSnapshot`.
- **None**: the recurring fee and usage overage are never invoiced. Each activation, renewal,
  trial conversion, plan change and quantity change issues a service statement listing plan,
  period, quantities and effective date, with no amounts. The price is stored as zero, so
  previews, proration and MRR read 0 with no special case. Every plan change on a no-price
  subscription applies immediately (nothing is paid, so there is no paid period to wait out),
  quantity decreases still wait for the renewal whose statement records them, and a trial's
  opening document is a statement too, so no invoice number is ever consumed. Overage is rated
  and kept as `NoCharge`. No-price works on a calendar-aligned yearly price, unlike a contract.
  _(Clarified 2026-11-09 during implementation.)_
- **Trial**: a plan with a trial starts `Trialing`; conversion is a renewal like any other.
- **Discount and campaign codes** apply at activation as for card subscriptions.
- **Billing profile** must be complete before activation, as for a paid card subscription.
- A card stored on the organization's billing account is never charged for an offline
  subscription.

Plan change pricing:

| Who changes plan | Subscription was | New plan is priced at |
|---|---|---|
| Tenant admin | any | contract, catalogue, or none, as the admin sets |
| Org admin | contract | the new plan's catalogue price (contract price dropped) |
| Org admin | catalogue | the new plan's catalogue price |
| Org admin | none | none; a statement records the change |

Priced plan changes, and quantity changes on catalogue pricing, prorate and issue a record
invoice as card changes do, with no charge.

### `SubscriptionFinancialDocument` (extended)

| Field | Notes |
|---|---|
| `Type` | adds `ServiceStatement = 3` |
| `CollectionMethod` | snapshot; `OfflineInvoice` renders "Payable by bank transfer" beside the merchant's `PaymentInstructions` |
| `Number` | statements draw from their own per-tenant sequence; invoice numbering stays gapless |

An offline record invoice has no payment id; nothing in the document refers to a charge.

## API Contract

New routes, console organization only (a caller from any other organization naming another
organization acts on their own, which then fails the permission):

| Verb | Route | Request | Response | Errors |
|---|---|---|---|---|
| POST | `/subscriptions/offline` | `organizationId`, `planCode`, `priceId`, `quantities`, `timeZoneId`, `discountCode?`, `pricing { mode, contractAmountMinor?, contractCurrencyCode? }` | 201 `SubscriptionResponse` | 400 `subscription_request_invalid`, 400 `subscription_billing_profile_incomplete`, 400 `subscription_offline_console_only` (permission held but caller is not the console), 400 `subscription_offline_contract_calendar_unsupported`, 403, 404 plan or price, 409 `subscription_already_active`, 409 `subscription_trial_already_used` |
| PUT | `/subscriptions/{id}/offline/pricing` | `pricing` | 200 `SubscriptionResponse` with `pendingOfflinePricing` | 400, 403, 404, 409 `subscription_not_offline`, 409 `subscription_not_live` |
| PUT | `/subscriptions/{id}/offline/plan` | as the existing plan change, plus `pricing` | as the existing plan change | as the existing plan change, plus 409 `subscription_not_offline` |

Existing routes:

- `PUT /subscriptions/{id}/plan`, `PUT /subscriptions/{id}/quantities` and their previews work on
  offline subscriptions with the pricing rules above, and never return a checkout URL.
- `DELETE /subscriptions/{id}` cancels with the same rules as card subscriptions.
- `POST /subscriptions/{id}/payment-method/setup` answers 409 `subscription_offline_collection`.
- Credit purchase and preview routes (spec 002 subscription credits, spec 005 organization
  credits) answer 409 `subscription_offline_collection` for an offline subscription, or an
  organization holding a live one.
- `SubscriptionResponse`, "current" and "mine" gain `collectionMethod`, `offlinePricing`,
  `pendingOfflinePricing`.
- Invoice list and PDF routes return statements with `type: ServiceStatement`.
- Revenue reports accept a `collectionMethod` filter.

Validation: `contractAmountMinor` and `contractCurrencyCode` required for `Contract` and forbidden
otherwise; amount greater than zero; currency one the `Payment` section knows the minor units of.

## Authorization

| Action | Required permission |
|---|---|
| Offline activation, re-pricing, offline plan change | `blocks-utilities::subscription-offline::manage` (new), console organization only |
| Plan change, quantity change, cancel | existing `blocks-utilities::subscription::manage`, own organization |
| Read offline fields, invoices, statements | existing read permissions |

## Edge Cases & Failure Modes

- **Organization already has a live or incomplete subscription**: activation answers 409, as
  create does today.
- **Stored card present**: ignored; an offline renewal never calls the billing gateway.
- **Renewal worker crash** after the period advances but before the document is issued: the
  existing financial-document announcement retry issues it; the period never advances twice.
- **Re-pricing twice before renewal**: the last write wins; only one pending pricing exists.
- **Re-pricing then cancel at period end**: the pending pricing is discarded with the
  subscription.
- **Org admin plan change from contract**: the contract price is dropped immediately for the
  proration and every renewal after it.
- **No-price overage**: usage is still recorded and enforced against limits; it is never
  invoiced.
- **Trial on no-price plan**: conversion issues a statement; nothing ever goes `Unpaid`.

## Non-Functional Requirements

- Volume: a handful of contract customers per tenant; no new latency budget.
- `TimeProvider` for every timestamp in new and changed code paths.
- No new indexes beyond the statement number sequence.

## Integrations

- **Payment module**: never called for offline subscriptions.
- **Financial documents**: record invoices and statements through the existing issuer,
  announcer and mail path; statement template derived from the invoice template without amounts.
- **Outbox events**: existing lifecycle events carry `collectionMethod`; new
  `SubscriptionOfflinePricingChanged` on re-pricing.
- **Audit trail**: activation, re-pricing and offline plan change recorded with the actor.
- **Worker**: renewal scheduling unchanged; offline branch inside `SubscriptionRenewalService`.
- **Client console**: activation and re-pricing page; customer badge; card prompts hidden.
- **Simulation harness**: offline create and run-due renewals.
- **OpenAPI**: new routes, fields and enum values documented.

## Acceptance Criteria

- **AC-1** Given a complete billing profile, when a console caller with offline permission
  activates plan `pro` for organization O with catalogue pricing, then O's subscription is
  `Active` with `CollectionMethod = OfflineInvoice`, no payment is created, and a record invoice
  marked "Payable by bank transfer" is issued and mailed.
- **AC-2** Given a contract price of 50000 CHF per month, when activated, then the invoice shows
  50000 net with the plan price's tax, and the plan's catalogue price does not appear.
- **AC-3** Given no-price mode, when activated, then a service statement with no amounts is
  issued and mailed, numbered from the statement sequence, and no invoice number is consumed.
- **AC-4** Given an offline subscription and no stored card, when its renewal is due, then it
  renews to the next period, stays `Active`, and issues a record invoice or statement.
- **AC-5** Given an offline subscription and a stored card on the billing account, when it
  renews, then the billing gateway is never called.
- **AC-6** Given an offline plan with a trial, when activated, then it is `Trialing`; when the
  trial ends, then it becomes `Active` with a record invoice or statement and never `Unpaid`.
- **AC-7** Given an incomplete billing profile, when activating, then 400
  `subscription_billing_profile_incomplete` and nothing is created.
- **AC-8** Given O already has a live subscription, when activating, then 409
  `subscription_already_active`.
- **AC-9** Given a caller with only `subscription::manage`, when calling any offline route,
  then 403 before module code runs.
- **AC-10** Given a contract-priced subscription, when the plan catalogue price is edited, then
  the next renewal invoice still shows the contract price.
- **AC-11** Given re-pricing from contract 50000 to 60000, then the current period is untouched
  and the next renewal invoices 60000; `pendingOfflinePricing` is visible until then.
- **AC-12** Given an org admin upgrades a contract-priced subscription, then the proration and
  following renewals use the new plan's catalogue price, with a record invoice and no charge.
- **AC-13** Given a tenant admin changes plan with a new contract price, then the proration
  uses that contract price.
- **AC-14** Given a no-price subscription, when the org admin changes plan or quantity, then it
  stays no-price and a statement records the change and its effective date.
- **AC-15** Given a contract-priced subscription, when quantity rises mid-period, then the
  quantity applies, nothing is charged, no document is issued, and the next renewal still
  invoices the contract amount. Given catalogue pricing, a prorated record invoice is issued.
- **AC-16** Given a catalogue- or contract-priced subscription with overage at period close,
  then a record usage invoice at the plan's meter rates is issued; given no-price, nothing is
  invoiced.
- **AC-17** Given an offline subscription, when payment-method setup is requested, then 409
  `subscription_offline_collection`.
- **AC-18** Given an offline subscription, when the org admin cancels at period end, then it
  ends at period end as a card subscription would, with no further documents.
- **AC-19** Given MRR reports, then contract and catalogue offline subscriptions count at their
  invoiced recurring amount, no-price at 0, and the `collectionMethod` filter isolates them.
- **AC-20** Given every existing subscription document, when read, then `CollectionMethod` is
  `AutomaticCharge` and behaviour is unchanged.
- **AC-21** Given the simulation harness, when an offline subscription is created and run-due
  advances past its renewal, then it renews with the right document and no payment.
- **AC-22** Given the console, when a tenant admin activates an offline subscription, then the
  customer's subscription page shows "Billed by invoice" and no card prompts.
- **AC-23** Given an offline subscription, when its organization previews or buys subscription
  credits or organization credits, then 409 `subscription_offline_collection`, nothing is
  charged or granted, and a tenant admin's free grant still succeeds.

## Deferred Decisions

| Decision | Fallback chosen now | Revisit when |
|---|---|---|
| Invoice due date | none; merchant `PaymentInstructions` only | Klax finance asks for net-N terms |
| Paid-status tracking | none; reconciled outside Utilities | Klax wants reminders or a receivables view |
| Contract start date other than now | activation starts the period | a contract starts on a fixed date |
| Credit purchases by offline customers | refused (409 `subscription_offline_collection`) | Klax wants credits on contract, by card or as record invoices |
| Contract price on a calendar-aligned yearly price (whose opening stub is priced from a separate monthly base) | refused (400 `subscription_offline_contract_calendar_unsupported`); catalogue pricing and anniversary prices work | a contract needs a calendar-yearly price and says what its opening stub costs |

## Open Questions

_None._
