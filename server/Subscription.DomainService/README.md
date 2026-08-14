# Subscriptions

Plans, subscriptions, entitlement and metered usage. Blocks owns all of it; a payment provider
moves the money.

Phase 1 got an organization onto a plan and answered *"what is this organization allowed to do
right now?"* fast and correctly. Phase 2 adds the billing clock: a subscription now renews on its
own, and a decline moves it through dunning to `PastDue` and then `Unpaid` without anyone
watching it happen. Invoices, tax, SCA recovery, proration and the rating of metered usage into a
bill are still later phases — their fields exist already, so those phases add transitions rather
than columns.

## The rule everything else follows from

**The platform never learns a domain word.** There is no `Seats` column and no
`ScreeningCount`. Quantities carry a unit label the product chooses; usage flows through meters
the product names; plan features are a JSON bag stored verbatim and never interpreted.

The test when adding a field: *would a digital-signature client recognise this name?* One
product sells seats and meters screenings; another sells workspaces and meters envelopes. Both
are configuration.

## What talks to what

Subscriptions depend on `Payment.DomainService`; nothing in payments may depend on
subscriptions. `XUnitTest/Subscription/SubscriptionBoundaryTests` fails the build if that
reverses. The surface relied on:

| Type | Used for |
| --- | --- |
| `IPaymentService.MakePaymentAsync` | raising the first charge through hosted checkout |
| `IPaymentRepository` | reading payment state during activation, and recovering a lost charge |
| `IStoredPaymentMethodRepository` | reading the provider's customer from the saved card |
| `IRecurringPaymentService.CreateRecurringPaymentAsync` | charging the stored card for a renewal or a dunning retry, off-session |
| `IPaymentExecutionContextResolver` | tenant, organization and actor from the request |
| `IPaymentTenantContextScopeFactory` | establishing a tenant for background sweeps |
| `ICurrencyMinorUnitResolver` | converting at the one boundary where money leaves this module |
| `PaymentFailureKind`, `ApiResponse<T>` | result and envelope shapes, reused not copied |
| `PaymentLogValue` | hashing identifiers before they reach a log |

## Organizations are subscribers, not merchants

The tenant holds the merchant configuration; its organizations are its customers. A tenant
registers one provider configuration at tenant level and every organization under it buys from
that account.

This is the opposite of what the payment module's own organization scoping is for, where an
organization may be a separate business with its own merchant account. Both models work — they
are distinguished by whether the provider configuration names an organization — but they must
not be confused, and a charge raised from here deliberately names no organization.

> **Fixed.** A saved card's provider token used to be encrypted under a key ring scoped to the
> *caller's* organization, which is right when organizations are merchants and wrong when they
> are customers — the token belongs to the tenant's merchant account. `StoredPaymentMethod` now
> carries `EncryptionOrganizationId`, resolved from the actual provider configuration at write
> time and independent of `OrganizationId` (which stays the caller's, for card-listing
> visibility). See `PaymentEncryptionScope.From(StoredPaymentMethod)` in the payment module.
>
> Cards saved **before** this fix carry no `EncryptionScopeResolvedAtUtc` and keep decrypting
> under the old derivation — `PaymentEncryptionScope.From` falls back to `OrganizationId` for
> them, which was correct at the time they were written because every organization was still a
> merchant. `Payment:FallBackToSharedEncryptionKeyRing` remains the safety net for those records
> until they are backfilled or re-saved; don't set it to `false` until they are.

## Status

`Incomplete → IncompleteExpired`, `Incomplete → Trialing | Active`, cancellation, renewal, and
dunning through `PastDue → Unpaid` are all driven now. Entitlement's answer for every status
below was already correct in phase 1 — phase 2 only added what reaches `PastDue` and `Unpaid` in
the first place.

| Status | Grants? |
| --- | --- |
| `Incomplete` | no — created, first charge unconfirmed |
| `IncompleteExpired` | no — the first charge never completed |
| `Trialing` | yes, subject to trial grants |
| `Active` | yes |
| `PastDue` | yes, during the grace period |
| `Unpaid` | no |
| `Canceled` | no |

Enum values are explicit because they are persisted. Adding a member without a number renumbers
everything after it and silently reinterprets stored documents — a canceled subscription reading
back as active, with nothing thrown. `SubscriptionEnumStabilityTests` pins them.

## Snapshots

The plan and price are **copied** onto the subscription when it is created, not referenced.

Entitlement is then one document read with no join, and editing the catalogue stops being
retroactive: a subscriber keeps the terms they were sold until something deliberately migrates
them. That is the correct billing semantic as well as the faster read.

## Periods are derived, never advanced

`BillingPeriodCalculator` is pure and static, and the instant is always a parameter. Asking
which period an instant falls in recomputes it; nothing has to notice a boundary passing.

So metered usage rolls over with no scheduled job and no possibility of a rollover running
twice or not at all. The counter's identifier contains the period key, so crossing a boundary
simply addresses a different document, which the next write upserts at zero.

Two rules inside it are worth knowing:

- **Month ends clamp on read, never on write.** An anchor on the 31st bills on the 28th in
  February and returns to the 31st in March. Persisting February's clamp would drag every later
  period earlier for the life of the subscription.
- **Daylight saving is resolved, not thrown.** A boundary inside a spring-forward gap moves to
  the first instant that exists; one that happens twice in autumn consistently takes the earlier.

> The runtime images must carry `tzdata`. They are Alpine, which ships neither it nor ICU, and
> without it every IANA identifier fails — in production only, since developer machines have a
> system time zone database. Both Dockerfiles install it.

## Two schedules

A subscription has a fee cadence and a usage cadence, and they are independent. The fee follows
the price; usage is always monthly. Waiting a year to settle metered usage on an annual plan is
a year of unsecured credit.

## Renewal and dunning

`SubscriptionRenewalProcessor` sweeps every live subscription whose `NextFeeBillingAtUtc` has
arrived and hands it to `SubscriptionRenewalService`, which charges it and applies the outcome —
one method for a normal renewal, a dunning retry, and a trial converting to paid, because all
three are "charge the stored card for the period that is due" and none needs to know which of
the three it is.

```
Active/Trialing --success--> Active (period advances, dunning cleared)
Active/Trialing --decline--> PastDue (attempt 1, retry scheduled)
PastDue         --decline, attempts remaining--> PastDue (attempt N, retry scheduled)
PastDue         --decline, attempts exhausted--> Unpaid
any             --no stored payment method--> Unpaid (immediately, no retries)
```

A subscription with no `BillingAccount.DefaultPaymentMethodId` skips dunning entirely and goes
straight to `Unpaid` — retrying a charge with nothing to charge cannot succeed on attempt two
any more than it did on attempt one. This is also how a trial that never took a card behaves at
its end: `TrialTerms.EndsAtUtc` is the subscription's `NextFeeBillingAtUtc` from the moment it is
created, so the sweep picks it up the same way it picks up a renewal, and finds no card to charge.

Renewal is called through `ISubscriptionBillingGateway`, owned by this module — everything that
decides *when* and *how much* to charge (this state machine, the amount calculator) depends only
on the interface, never on how the charge is actually raised.
`SubscriptionBillingGatewayResolver` picks which implementation by
`SubscriptionChargeRequest.ProviderName`:

- **Stripe → `StripeInvoiceBillingGateway`.** Raises a standalone Stripe Invoice per attempt — an
  item, the invoice itself (`auto_advance=false`, so Blocks controls every step rather than
  Stripe's own background job), finalize, pay, and a void on decline. **No Stripe Subscription
  object exists behind this**, on purpose: a real Subscription would run Stripe's own Smart
  Retries and billing clock in parallel with this one, and the two would drift — Phase 1 rejected
  creating one for exactly that reason, and this task does not revisit it. Dunning is therefore
  still entirely Blocks' own responsibility; what changes is that a successful renewal now
  produces a real Stripe Invoice document (line item, invoice number, a path to Stripe Tax later)
  instead of a bare PaymentIntent.
- **Everything else → `RecurringChargeBillingGateway`.** The plain off-session PaymentIntent
  charge through `IRecurringPaymentService`, unchanged since it was written. A subscriber on Adyen
  needs no new code — the resolver falls through to this gateway for any non-Stripe provider name,
  so Adyen's behavior is exactly what it was before the Stripe Invoice gateway existed.

`StripeInvoiceBillingGateway` claims and unprotects the stored card directly against
`Payment.DomainService`'s repositories (`IStoredPaymentMethodRepository.TryClaimForPaymentAsync`,
`IProviderTokenProtector.UnprotectAsync`) rather than through `RecurringPaymentInitiationService`:
that service is built around `PaymentDetail`'s Authorized/Captured/Refused model, which an
invoice's draft/open/paid/uncollectible lifecycle does not map onto — routing through it would
teach the payment module the word "Invoice," which is exactly what this module's dependency rule
exists to prevent. `Payment.DomainService` is otherwise untouched by this beyond a new, standalone
Stripe Invoice HTTP client (`Payment.DomainService/Providers/Stripe/StripeInvoiceClient.cs`) that
nothing else calls.

Each renewal attempt gets its own order id, scoped to the period it charges
(`sub:{subscriptionId}:{periodKey}`), because the payment module allows only one recurring
payment per order id, ever — a shared order id across periods would reject every renewal after
the first. The idempotency key additionally carries the attempt number
(`sub-renew:{subscriptionId}:{periodKey}:{attempt}`): unlike the initial charge, a dunning retry
is a genuinely new attempt, not a replay of the one before it. `StripeInvoiceBillingGateway` in
turn suffixes that key per Stripe call (`:item`, `:invoice`, `:finalize`, `:pay`) — Stripe scopes
idempotency by endpoint, and the same raw key sent to four different endpoints in immediate
succession is not something to trust silently.

A discount reduces a renewal only while `DiscountTerms.DurationPeriods` and `ExpiresAtUtc` still
allow it; `SubscriptionDetail.DiscountPeriodsApplied` tracks how many periods it has already
reduced, so an expired discount's absence is detected without re-deriving history from past
charges.

`Subscription:DunningMaxAttempts` (default 4, including the first decline) and
`Subscription:DunningRetryIntervalHours` (default 24, a fixed interval rather than exponential
backoff — this is a business cadence for asking a customer to fix a card, not load-shedding
against a failing dependency) govern the cycle.

## Plan changes and proration

`PUT /api/subscriptions/{id}/plan` moves a live subscription to a different price mid-period.
`SubscriptionProrationCalculator` prices the change by comparing the unused value of the current
period against the cost of the same remaining time on the target price, both run through the
exact gross-and-discount math a renewal uses
(`SubscriptionAmountCalculator.GrossAmountMinor`/`ApplyDiscount`, made `internal` so this can
reuse them rather than duplicate them) — the subscriber's discount applies to both sides
identically, since it belongs to them, not to whichever plan they happen to be on.

**An upgrade is charged immediately** for the prorated difference, through the same
`ISubscriptionBillingGateway` a renewal uses — this is the seam's third caller. A decline leaves
the subscription untouched; no partial change is ever written. **A downgrade is never charged.**
Its value is banked as `SubscriptionDetail.CreditBalanceMinor` and spent automatically — an
existing balance is applied to a later upgrade before anything new is charged, and any of it
still unspent is consumed by the next renewal, `PeriodAmountMinor` subtracting it after the
discount and never below zero. **There is no refund path.** A credit is only ever applied to a
future charge; it is never paid out, and nothing in this module ever produces a negative amount.

**A trial changes plan with no charge and no credit at all** — nothing has been paid for yet, so
there is nothing to prorate. The plan, price and quantity snapshot simply swap.

Two restrictions keep this a contained piece of work rather than a rewrite of the billing clock:

- **Same currency, same billing interval only.** A different interval would mean rebuilding
  `FeeSchedule` and the current period's boundaries mid-flight, which is a separately-tricky
  problem this does not attempt — refused as a validation error rather than attempted.
- **`Trialing` and `Active` only.** `PastDue`/`Unpaid` is refused as a conflict: a customer who
  owes money changing plans is a support decision, not something to automate.

> **Known gap.** If a charge succeeds but the compare-and-set that records the plan change loses
> a race immediately after, the money has moved and the write has not. This is the same shape of
> risk the initial checkout has — and that one has a dedicated recovery sweep
> (`SubscriptionActivationProcessor.RecoverStaleAsync`) for exactly this reason. A plan change
> does not have an equivalent sweep yet; the case is rare (a genuine concurrent write to the same
> subscription, not a network failure) and is called out here rather than left to be discovered.

## Entitlement is advisory; recording is enforcement

`GET /api/entitlements` reads only our own database — the subscription, then one counter per
metered entitlement. `EntitlementService` takes no provider gateway and no HTTP client in its
constructor, which is how the guarantee is held: **if the provider is down, every existing
customer keeps working.**

It is deliberately *advisory* for anything metered. Two callers at 499 of 500 will both be told
they have one left. The authoritative answer is the balance returned by
`POST /api/subscription-usage`, which already includes the caller's own contribution — so the
two get different answers and only one of them is over.

A caller that must not exceed an allowance sets `enforce` and acts on `allowed`. A refused call
is rolled back with a compensating entry, leaving the balance where it was.

The subscription is cached for `Subscription:EntitlementCacheSeconds` (default 10) and
invalidated on change. **Usage counters are never cached** — they are the volatile half, and a
stale one would let a caller past an allowance already spent. `?fresh=true` bypasses the cache.

## The usage ledger

Append-only. A correction is a `Reversal` entry, never an edit, so the history can always
explain a bill.

The ledger is written **before** the counter. A crash between the two leaves the counter
under-counting, which can be recomputed from the ledger; the other order over-counts with
nothing left to prove it, and the customer is billed for it.

An idempotency key is mandatory, unique per subscription and meter. At-least-once delivery makes
a repeated call a certainty rather than a risk.

Counters expire (`Subscription:CounterRetentionDays`, default 400). **The ledger never does.**

> **Keep identifying data out of `metadata`.** Billing needs a count, not a dossier. This is a
> shared billing store, retained for years and exported for invoicing; anything naming a person
> belongs in the calling product's own records with an opaque reference here.

## Events

Appended in the same write as the state change that caused them, then published to
`blocks_subscription_lifecycle_topic` by a processor. MongoDB and the bus share no transaction,
so publishing inline would drop events precisely when something went wrong.

`SubscriptionCreated`, `SubscriptionTrialStarted`, `SubscriptionActivated`,
`SubscriptionActivationFailed`, `SubscriptionCancellationRequested`, `SubscriptionCanceled`,
`UsageThresholdReached`, `SubscriptionRenewed`, `SubscriptionRenewalFailed`,
`SubscriptionPastDue`, `SubscriptionUnpaid`, `SubscriptionPlanChanged`.

> **Nothing consumes this topic, and there is no email path in this repository** —
> `Notification.DomainService` and `Mail.DomainService` contain only build output, no source. A
> quota threshold raises an event and has no user-visible effect in this phase. That is the
> intended shape: the platform states the fact, each product decides what it means.

Every event carries a correlation id, persisted at write time, because publication happens later
in another process. Without it the trace ends at the queue.

## Activation waits for the webhook

A subscription becomes active only when the payment carries both a confirming status **and**
`WebhookConfirmedAtUtc`. The shopper's return from checkout is not evidence: a redirect can be
replayed, forged, bookmarked, or lost when someone shuts the laptop.

Clients should therefore expect a brief `Incomplete` window after paying — the browser usually
comes back before the webhook lands.

Activation runs on the payment work tick, which every inbound webhook already dispatches, so it
happens within milliseconds. `SubscriptionReconciliationBackgroundService` is the safety net for
what no message carries: a compare-and-set lost to a worker that then crashed, a charge raised
but never recorded, a webhook that arrived during a restart.

> The sweep does nothing unless `Subscription:TenantIds` is set, and says so loudly at startup.
> Nothing else discovers tenants.

## Settings

Under the `Subscription` section. The ones whose default is a decision:

| Setting | Default | Why it matters |
| --- | --- | --- |
| `TenantIds` | *(empty)* | Which tenants the sweep covers. An omitted tenant is never reconciled. |
| `EntitlementCacheSeconds` | `10` | How stale an entitlement answer may be. Counters are never cached. |
| `CounterRetentionDays` | `400` | How long a finished period's counter is kept. Long enough for a billing dispute. |
| `InitialChargeGraceMinutes` | `60` | How long an unpaid subscription waits before it is treated as abandoned. |
| `ReconciliationPollSeconds` | `120` | Clamped to a 30 second minimum. |
| `RenewalBatchSize` | `50` | How many due subscriptions one sweep pass takes. |
| `DunningMaxAttempts` | `4` | Attempts, including the first decline, before a subscription moves to `Unpaid`. |
| `DunningRetryIntervalHours` | `24` | Fixed interval between dunning attempts. |
| `MaximumUsageMetadataEntries` | `10` | Bounds what a product can attach to a billing record. |

A currency must also exist in `Payment:CurrencyMinorUnits` or a price in it can never be
charged. That is validated when the price is authored rather than at checkout, where the same
mistake reaches a customer who has already chosen a plan.

## Before the first tenant goes live

Provision the tenant-level payment key ring — `payment-keyring-{tenantSlug}` via
`scripts/payment-key-vault/Provision-PaymentKeyRing.ps1`. Provider registration fails closed
without one.

## Testing

```bash
dotnet test server/XUnitTest/XUnitTest.csproj --filter "FullyQualifiedName~XUnitTest.Subscription"
```

Uniqueness, atomic increments and compare-and-set are enforced by MongoDB, not by this code, so
the tests that prove them live in `XUnitTest/Integration` and need a running mongod
(`BLOCKS_IT_MONGO` to point elsewhere). Mocking those would test the mock.

**None of this has met live provider traffic.** Every Stripe defect this project has had was
found by real traffic while the unit suite stayed green, and the off-session charge path this
module builds on has itself never been exercised live. Treat a green suite as necessary and not
sufficient.
