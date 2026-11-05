# Spec 005: Standalone Credit Purchase

**Status:** Approved (2026-11-05)
**Date:** 2026-11-05
**Tracks:** [#672](https://github.com/SELISEdigitalplatforms/blocks-utilities/issues/672)
**Builds on:** spec 002 (purchasable meter credits), spec 003 (credit checkout on first top-up)

## Problem

Credits can only be bought by a subscriber, for a meter on the plan they hold, at the price that
plan snapshotted (spec 002). An organization with no subscription cannot buy anything, and an
organization with one cannot buy credits priced independently of its plan. The client (Klax) wants
an organization admin to buy a known quantity of a meter's credits from a tenant-wide offer, pay
instantly, and have those credits belong to the organization: usable with or without a
subscription, surviving a later subscription, and expiring on the offer's own terms.

Success: an organization admin previews and buys credits for a meter from the tenant's offer,
pays on the stored card or at a hosted checkout, receives an invoice in an organization-level
list, and sees usage draw on those credits, including when the organization holds no
subscription at all.

## Scope

### In

- **Meter catalogue**: meter definitions (identity and counting semantics only), tenant-wide with
  an optional per-organization override, resolved the way plans are. Plans pick meters from it; a
  meter authored inside a plan is added to it. Archive, no delete.
- **Credit offer**: one per catalogue meter per scope (tenant, or one organization's override),
  versioned like discounts: price per currency, min and
  max quantity, tax terms, expiry rule (fixed duration from purchase, or never), spend-order
  switch, enabled flag.
- **Organization credit purchase**: preview, idempotent purchase, discount code, stored-card
  charge or hosted checkout (spec 003 path), invoice, recovery sweep.
- **Organization credit grants**: ledger with per-grant remaining and expiry, admin free grants
  with a reason, grant history, maintenance sweep.
- **Usage gate**: consumes organization credits with or without a subscription, in the order the
  offer's switch says. With no subscription and no live credits, usage is refused outright.
- **Organization-level financial document list** holding standalone credit invoices.
- **Reporting**: current usage, "mine" and reports show organization credits as their own line.
- **Simulation harness**: subscription state lists the organization's standalone grants and
  purchases; run-due-jobs runs the organization-credit sweeps; a new organization-scoped
  simulation route serves organizations with no subscription.
- Outbox events, audit trail, usage projection, OpenAPI docs.

### Out

- Refunding unused credits (non-refundable, as spec 002 decided).
- Moving credits between organizations, or between the organization pool and a subscription
  seat or pool.
- Multiple offers (packs) per meter.
- Tiered or bulk pricing.
- Backfilling the catalogue from existing plan meters: the catalogue starts empty and existing
  plans keep working without an entry.
- Changing spec 002 plan-level top-up terms: they stay as they are, a separate price source.
- Deleting catalogue meters.

## Domain Model

### `MeterCatalogueEntry` (new collection)

Identity and counting semantics only. No money, no expiry, no spend order: the catalogue is
reused by plans and by offers, and each of those prices and expires credits its own way.

| Field | Notes |
|---|---|
| `ItemId`, `TenantId` | |
| `OrganizationId` | nullable, as `Plan.OrganizationId`: an organization's own entry wins, otherwise the tenant's |
| `MeterKey` | unique per tenant and organization scope, immutable once created |
| `DisplayName`, `UnitLabel` | |
| `Aggregation`, `QuantityScale` | as `PlanMeter` |
| `Status` | `Active`, `Archived` |
| `CreatedAtUtc`, `LastUpdatedAtUtc`, `Version` | |

Plan authoring: the plan builder lets an author pick a catalogue meter (key, display name, unit,
aggregation, scale copied onto the `PlanMeter`) and then set everything a plan meter has today
(included quantity, reset policy, overage, limits, rate tables, top-up terms). A meter created
inside a plan with a key not yet in the catalogue is added to the catalogue on plan save. An
existing plan whose meter key has no catalogue entry keeps working unchanged.

### `CreditOffer` (new collection, one per meter per scope)

| Field | Notes |
|---|---|
| `ItemId`, `TenantId`, `MeterKey` | `MeterKey` unique per tenant and organization scope; must reference a catalogue entry resolvable in that scope |
| `OrganizationId` | nullable: a buying organization's own offer wins, otherwise the tenant's. How a seller gives one customer a special price |
| `Enabled` | pause sales without losing prices |
| `UnitPrices` | one `{ CurrencyCode, UnitAmountMinor }` per currency |
| `MinQuantity`, `MaxQuantity` | whole credits; max required, it bounds a single charge |
| `TaxRateBasisPoints`, `TaxMode` | as `MeterTopUpTerms` |
| `Expiry` | `Never`, or `FixedDuration` with a `CreditExpiryDuration` |
| `SpendOrder` | `OrganizationCreditsFirst` or `SubscriptionFirst` |
| `Version`, audit stamps | versioned like `Discount`: a purchase records the version it was quoted from |

### `OrganizationCreditGrant` (new collection)

As `SubscriptionCreditGrant` without `SubscriptionId`, `SeatNumber`, `PlanVersionAtGrant`,
`ExpiryPolicy`. Keyed by `TenantId`, `OrganizationId`, `MeterKey`. `ExpiresAtUtc` is computed at
grant time from the offer's `Expiry` and never changes after; null means never. `Source` is
`Purchased` or `AdminGranted`. `Remaining` moves only through conditional updates, as today.

### `OrganizationCreditPurchase` (new collection)

As `SubscriptionCreditPurchase` with `OrganizationId` in place of `SubscriptionId`. Id is
`{organizationId}:{idempotencyKey}`. Carries `OfferVersion`, the chosen `CurrencyCode`, and the
same provider, checkout, payment, grant and failure fields, so the spec 002/003 recovery sweep
logic applies unchanged.

### `SubscriptionFinancialDocument` changes

`SubscriptionId` becomes nullable; a standalone credit invoice has none. `Subject` describes the
offer and meter. Documents are listed per organization as well as per subscription.

### Consumption order

For one usage recording of meter `K` in organization `O`:

1. Resolve subscriptions as today (seats first, then the organization's own).
2. Collect `O`'s live organization grants for `K`, in consumption order (earliest `ExpiresAtUtc`
   first, undated last, oldest first within a tie, id last).
3. If `K` has an offer with `SpendOrder = OrganizationCreditsFirst`, spend organization grants
   before the subscription path (plan allowance, then subscription grants, then overage).
   Otherwise spend organization grants only after the subscription path is exhausted and before
   overage would be recorded.
4. With no resolvable subscription metering `K`: spend organization grants only. When none are
   live, refuse the recording regardless of `Enforce`, with the message "No credits left for this
   meter. Buy more to continue." Nothing is recorded and nothing is billed.
5. With no subscription and no offer and no catalogue entry for `K`: refuse as an unknown meter.

`SpendOrder` is read live at usage time. Expiry is copied onto the grant at purchase time.

### Expiry and lifecycle

Organization grants keep their own expiry. A later subscription, plan change, renewal or
cancellation never forfeits, shortens or extends them. Disabling the offer or archiving the meter
refuses new purchases only; live grants stay spendable until exhausted or expired. The daily
maintenance sweep closes expired and exhausted organization grants and raises the exhausted
event, as it does for subscription grants.

## API Contract

Payloads wrapped in `ApiResponse<T>`; failures use the existing result-to-status mapping.

| Verb | Route | Request | Response | Errors |
|---|---|---|---|---|
| GET | `meter-catalogue` | `?status&organizationId` | entries | 403 |
| GET | `meter-catalogue/{meterKey}` | | entry | 403; 404 |
| POST | `meter-catalogue` | `{ organizationId?, meterKey, displayName, unitLabel, aggregation, quantityScale }` | entry | 400; 403; 409 key exists in scope |
| PUT | `meter-catalogue/{meterKey}` | same minus `meterKey` | entry | 400; 403; 404 |
| POST | `meter-catalogue/{meterKey}/archive` | | entry | 403; 404; 409 already archived |
| GET | `credit-offers` | `?meterKey&enabled&organizationId` | offers | 403 |
| GET | `credit-offers/{meterKey}` | | offer | 403; 404 |
| POST | `credit-offers` | `organizationId?` + offer fields | offer | 400; 403; 404 no catalogue entry; 409 offer exists in scope or meter archived |
| PUT | `credit-offers/{meterKey}` | offer fields | offer, version raised | 400; 403; 404 |
| POST | `organization-credits/preview` | `{ organizationId?, meterKey, quantity, currencyCode, discountCode? }` | quote: unit price, gross, discount, net, tax, total, currency, `expiresAtUtc?` | 400; 404 offer; 409 offer disabled, meter archived, currency not priced, quantity out of bounds; 422 discount not applicable |
| POST | `organization-credits` | preview fields + `idempotencyKey` | `{ grant?, purchase, documentId?, checkoutUrl? }` | as preview; 402 card declined; 409 key reused with different payload |
| GET | `organization-credits` | `?organizationId&meterKey&status` | grants | 403 |
| POST | `organization-credits/grants` | `{ organizationId?, meterKey, quantity, reason }` | grant | 400 reason missing; 403; 404 catalogue entry |
| GET | `organization-credits/purchases` | `?organizationId&status` | purchases | 403 |
| GET | `organization-credits/invoices` | `?organizationId` | financial documents with no subscription | 403 |
| GET | `subscription-simulation/organizations/{organizationId}/state` | | organization grants, purchases, pending checkouts, due work | 403; 404 |
| POST | `subscription-simulation/organizations/{organizationId}/jobs/run-due` | as subscription run-due | work run | 403 |

`organizationId` in a body or query is honoured only for the platform console, as every
subscription request does; otherwise the caller's own organization is used. On catalogue and
offer routes it selects the organization override being authored or listed; omitted by the
console, it addresses the tenant-wide entry. Preview and purchase resolve the offer for the
buying organization: its own override first, otherwise the tenant's.

Existing responses extended: `UsageResponse`, `SubscriptionUsageCurrent` and the "mine" response
gain `OrganizationCreditsRemaining` and `OrganizationCreditsConsumed`; `PlanResponse` meters carry
`catalogueMeterKey`; `SubscriptionSimulationStateResponse` gains `organizationCredits`.

Idempotency: same key and same payload returns the stored result at whatever stage it reached;
same key with a different payload is refused.

## Authorization

| Action | Required permission |
|---|---|
| Catalogue and offer read | `blocks-utilities::subscription-plan::read` |
| Catalogue and offer authoring | `blocks-utilities::subscription-plan::manage` |
| Preview / buy organization credits | `blocks-utilities::subscription::manage` |
| List grants, purchases, invoices | `blocks-utilities::subscription::manage` |
| Free grant | `blocks-utilities::subscription::manage` |
| Organization simulation routes | existing simulation permission |

Tenant boundary: every read and write is scoped by `TenantId` and `OrganizationId`. A caller may
only act on their own organization unless they are the platform console.

## Edge Cases & Failure Modes

- **Currency drift**: the buyer picks the currency from the offer's price list on each purchase.
  No lock; an organization may hold grants bought in different currencies, because a grant is
  credits, not money. Only the invoice carries a currency.
- **Offer edited after purchase**: a grant's expiry is fixed at purchase time. `SpendOrder` is read
  live, so flipping it changes consumption for everyone immediately.
- **Offer disabled, meter archived**: new purchases and free grants refused; live grants spend on.
- **Seats**: organization credits have no buckets. On a per-person plan they are spent from the
  organization pool whichever seat used them.
- **Concurrency**: `Remaining` is only decremented by conditional update; two recordings cannot
  both spend the last credit.
- **Crash between charge and grant**: the pending purchase record plus the recovery sweep finish
  the job from the provider order id, as in spec 002 and 003.
- **Checkout abandoned**: purchase fails a grace period after `CheckoutExpiresAtUtc`.
- **Plan meter key not in catalogue**: existing plans are untouched; the catalogue is populated on
  the next plan save that introduces a key.
- **Catalogue key reuse after archive**: an archived entry keeps its key; creating the same key
  again is a 409. Un-archiving is an update back to `Active`.

## Non-Functional Requirements

- Same order of magnitude as subscription credits; no new latency budget.
- Usage recording gains at most one indexed read of live organization grants per call; index on
  `(TenantId, OrganizationId, MeterKey, Status, ExpiresAtUtc)`.
- `TimeProvider` for every timestamp in the new services.

## Integrations

- **Payment module**: charge or hosted checkout through the existing billing gateway with the
  organization's billing account; a new order-id prefix distinguishes organization credit
  purchases.
- **Financial documents**: one invoice per purchase, same lines as spec 002, delivered through
  the existing mail path, listed under `organization-credits/invoices`.
- **Outbox events**: `OrganizationCreditsPurchased`, `OrganizationCreditsGranted`,
  `OrganizationCreditsExhausted`, carrying organization, meter, quantity and remaining.
- **Usage projection**: republished after every organization grant mutation and after
  consumption, carrying the new fields.
- **Audit trail**: catalogue and offer changes, purchase, grant and expiry as audit events.
- **Worker**: organization purchase recovery and grant maintenance added to the existing credit
  work types.
- **Client console**: catalogue and offer authoring pages; plan builder meter picker; organization
  credits page (buy, grants, purchases, invoices); simulation harness organization view.
- **OpenAPI**: new endpoints and fields documented.

## Acceptance Criteria

- **AC-1** Given an offer for meter `ai` at 10 cents per credit in USD and CHF, when a manage
  caller previews 100 credits in CHF, then the quote is in CHF at the CHF unit price.
- **AC-2** Given the same offer and a valid credit-scoped 20% code, when previewing 100 credits,
  then discount is 200 and net is 800 in minor units.
- **AC-3** Given a stored card, when buying 100 credits with a fresh idempotency key, then the card
  is charged once, a grant of 100 appears with `Remaining` 100, and an invoice with no
  subscription id is listed under the organization's invoices.
- **AC-4** Given no stored card, when buying, then the response carries a checkout URL and the
  purchase is `AwaitingPayment`; when the payment settles, the grant is written and the card is
  offered to the billing account.
- **AC-5** Given the same key and payload sent twice, then the second call returns the first
  result with `replayed` set and no second charge.
- **AC-6** Given the same key with a different quantity, then the call is refused with 409.
- **AC-7** Given an offer with expiry `FixedDuration` of 30 days, when buying, then the grant's
  `ExpiresAtUtc` is purchase time plus 30 days; with expiry `Never`, it is null.
- **AC-8** Given an organization with no subscription and 10 live credits, when 4 units of usage
  are recorded, then `Remaining` is 6 and nothing is billed.
- **AC-9** Given an organization with no subscription and 0 live credits, when usage is recorded
  with `Enforce` false, then the call is refused with "No credits left for this meter. Buy more to
  continue." and nothing is recorded.
- **AC-10** Given an organization with no subscription and a meter key with neither offer nor
  catalogue entry, when usage is recorded, then the call is refused as an unknown meter.
- **AC-11** Given a subscription with 10 allowance and 10 organization credits and the offer set
  to `SubscriptionFirst`, when 15 units are recorded, then allowance is 0 and organization
  `Remaining` is 5.
- **AC-12** Given the same state with the offer set to `OrganizationCreditsFirst`, when 15 units are
  recorded, then organization `Remaining` is 0 and allowance is 5.
- **AC-13** Given a subscription with allowance, subscription grants and organization credits and
  `SubscriptionFirst`, when usage exceeds allowance, then subscription grants are spent before
  organization credits, and overage is recorded only after both are exhausted.
- **AC-14** Given a credits-only organization with live credits, when it subscribes and later
  changes plan and renews, then the organization grants are unchanged.
- **AC-15** Given the offer's expiry is edited from 30 to 60 days, then existing grants keep their
  original `ExpiresAtUtc`.
- **AC-16** Given the offer is disabled, then preview and purchase answer 409 and usage still
  draws on live grants.
- **AC-25** Given a tenant offer for `ai` at 10 cents and an organization override at 8 cents,
  when that organization previews, then the unit price is 8 cents; any other organization gets 10.
- **AC-26** Given only a tenant catalogue entry for `ai`, when a plan or offer in an
  organization's scope references `ai`, then the tenant entry resolves.
- **AC-17** Given a catalogue meter referenced by a plan or an offer, when archived, then it stays
  readable, its plan and offer keep working, new offers on it are refused, and creating the same
  key again answers 409.
- **AC-18** Given a plan saved with a meter whose key is not in the catalogue, then a catalogue
  entry with that key, display name, unit, aggregation and scale exists afterwards.
- **AC-19** Given a free grant request without a reason, then 400; with a reason, a grant with
  `Source = AdminGranted` and the offer's expiry.
- **AC-20** Given a grant expired by the clock, when the maintenance sweep runs, then it is closed
  and an `OrganizationCreditsExhausted` event is raised; the same when `Remaining` reaches 0.
- **AC-21** Given a simulated subscription whose organization holds standalone grants, when its
  simulation state is read, then those grants and purchases are listed; when run-due runs, then
  the organization sweeps run.
- **AC-22** Given an organization with no subscription, when its organization simulation state is
  read, then its grants, purchases and due work are listed, and run-due runs the sweeps.
- **AC-23** Given usage current, "mine" and reports, then organization credits remaining and
  consumed appear as their own figures beside plan allowance and subscription grants.
- **AC-24** Given a caller without manage permission, then every organization-credits route
  answers 403 before module code runs; a non-console caller naming another organization acts on
  their own.

## Deferred Decisions

| Decision | Fallback chosen now | Revisit when |
|---|---|---|
| Purchase confirmation email beyond the invoice mail | invoice mail only, as spec 002 | the client asks for a separate confirmation |
| Catalogue backfill from existing plans | none; populated on next plan save | an author needs offers for meters on untouched plans |
| Packs (several offers per meter) | one offer per meter | the client asks for starter and bulk packs |

## Open Questions

None.
