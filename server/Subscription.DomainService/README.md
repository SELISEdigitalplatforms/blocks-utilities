# Subscriptions

Plans, subscriptions, entitlement and metered usage. Blocks owns all of it; a payment provider
moves the money.

This is phase 1. An organization can be put on a plan, and the product can ask *"what is this
organization allowed to do right now?"* and get a fast, trustworthy answer. Renewal billing,
invoices, tax, dunning, proration and the rating of metered usage are later phases — their
fields and status values exist already, so those phases add transitions rather than columns.

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

> **Known issue, tracked separately.** A saved card's provider token is encrypted under a key
> ring scoped to the *caller's* organization. That is right when organizations are merchants and
> wrong when they are customers: the token belongs to the tenant's merchant account. Today
> `Payment:FallBackToSharedEncryptionKeyRing` (default `true`) absorbs it silently. Setting that
> flag to `false` — which the payment README's migration tells you to do — would make every
> customer organization's saved card undecryptable at once. Fix the scope to follow the resolved
> provider's organization before this module drives card saves at volume.

## Status

`Incomplete → IncompleteExpired`, `Incomplete → Trialing | Active`, and cancellation are the
transitions phase 1 drives. `PastDue` and `Unpaid` need a billing clock to reach, but
entitlement already answers correctly for them.

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
`UsageThresholdReached`.

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
