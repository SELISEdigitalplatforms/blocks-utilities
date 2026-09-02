# Subscriptions

Plans, subscriptions, entitlement and metered usage. Blocks owns all of it; a payment provider
moves the money.

Phase 1 got an organization onto a plan and answered *"what is this organization allowed to do
right now?"* fast and correctly. Phase 2 added the billing clock: a subscription renews on its
own, a decline moves it through dunning to `PastDue` and then `Unpaid`, a Stripe renewal produces
a real invoice document, a mid-period plan change is prorated, metered overage is priced from the
plan's rate tiers and charged as its own, independent invoice, and every one of those charges can
carry tax. SCA recovery is still later work.

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

## Console organization override

Every request DTO in this module has an optional `organizationId` field (a body field on writes,
an `?organizationId=` query parameter on reads), and every endpoint resolves the caller's
organization through `IPaymentOrganizationResolver` — the same policy `POST /api/payments/create`
and provider registration already use, reused rather than duplicated. **Whether naming one has any
effect depends on who is asking**, exactly as in the payment module:

| Caller | Its own organization | What `organizationId` in the request does |
| --- | --- | --- |
| The platform console | fixed, `Payment:ConsoleOrganizationId` (default `default`) | decides the organization |
| An application using the API | its own, from its token | nothing — ignored, and the caller's own is used |

This exists because the Blocks Utilities portal simulates every action as the console, and the
console's token always carries the same fixed organization for every tenant. Before this, every
subscription action performed from the portal landed on whatever `default` resolved to, with no
way to act on behalf of a real organization while testing. `x-blocks-key` and the bearer token
still decide the tenant exactly as before — this only widens who the *organization* may be, and
only for the console.

`SubscriptionContextResolver` is the single place this is applied: it resolves the tenant and
actor from `BlocksContext` as before, then hands the request's `organizationId` (if any) to
`IPaymentOrganizationResolver.ResolveAsync` alongside that context, exactly the way
`PaymentReservationService` already does for a payment. One exception remains, unchanged from
before this existed: if the *resolved* organization is still blank — a caller with no organization
at all, console or not — the request fails closed as `subscription_organization_missing` rather
than falling back to a tenant-wide, unscoped answer. A subscription belongs to an organization or
it belongs to nothing; there is no in-between reading to fall back to.

No new configuration: `Payment:ConsoleOrganizationId`, `Payment:IamBaseUrl` and
`Payment:VerifyOrganizationWithIam` govern this exactly as they already govern payments. Moving
the console, or turning the override off entirely, is a single change that affects both modules
at once — see the payment module's own README section on `ConsoleOrganizationId` for the full
detail on the magic-value trade-off and the IAM verification it goes through.

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

## Editing a plan ends when the first subscriber arrives

`PUT /subscription-plans/{planId}` rewrites what a plan sells. It refuses with
`subscription_plan_in_use` as soon as anything has subscribed, in any status — cancelled included.

That falls straight out of the snapshot rule above. An edit reaches the catalogue and nothing
else, so a plan that was sold would leave the catalogue saying one thing while every live
subscription bills from its own copy of something older; a cancelled subscription's past invoices
were computed from those terms too. Create a new plan and migrate instead — that is what
`ChangePlanAsync` is for.

The code and the organization come from the stored plan, never the request: a code is what
configuration points at, and a scope change would move the plan out from under whoever can see it.
Prices are separate documents and are untouched. Reads return `hasSubscribers` so a caller can say
why editing is closed before offering it.

`PlanDefinitionRequestValidator` holds every rule about a plan's contents, and both creating and
editing include it — an edit that could store what a create would have refused is a hole, and one
rule is the only way to be sure the two agree.

## Archiving a plan takes it off the menu for good

`PUT /subscription-plans/{planId}/archive?organizationId=` moves a plan from `Active` to
`Archived`. There is no restore, and no request body: the URL names the state the plan should be
in, which is why it is a `PUT` and why repeating it is safe.

This follows the same snapshot rule as everything above. Archiving reaches the catalogue and
nothing else, so everyone already subscribed carries on untouched — renewals, usage rating,
entitlements, invoicing and cancellation all continue from the terms copied onto each subscription
when it was sold. None of the plan's prices is rewritten or deleted. What stops is selling.

Refused with `subscription_plan_archived`:

* creating a subscription, and the purchase preview
* a plan-change preview, and the change itself
* `PUT /subscription-plans/{planId}` — editing the plan
* `POST /subscription-plans/prices` — adding a price
* `PUT /subscription-plans/prices/{priceId}/tax` and `/discount`
* `PUT /subscription-plans/prices/{priceId}/archive` — retiring one of its prices

All five catalogue mutations exist only to change what a *future* subscriber pays, and an archived
plan has no future subscriber. Reading is deliberately still open: inspecting an archived plan is
what the `Archived` filter is for, and duplicating one is how a replacement gets made.

Only `Active` is archivable. A `Draft` plan answers as not found, which is what a draft is to every
catalogue view — there is no menu to take it off, and archiving is permanent enough that stranding
a plan in a state it was never sellable from would be worse than refusing.

### Repeating it, and racing it

The write is conditional on the plan still being `Active` **and** still at the version just read,
and `ModifiedCount == 0` covers three different situations that need three different answers. The
service re-reads to tell them apart:

| What happened | Answer |
| --- | --- |
| Already `Archived` | success, no second write (`AlreadyArchived`) |
| Another archive won the race | success — both callers wanted this state and it is the state |
| An unrelated edit moved the version on | `subscription_plan_changed` |
| Gone, or not visible to the caller | the ordinary not-found response |

The idempotency is the one place this differs from `ArchivePriceAsync`, which reports a repeat as a
conflict. A price is one of several on a live plan, so repeating that call usually means the caller
has lost track of which; archiving a plan twice is the same request arriving twice, and has one
sensible answer.

### Listing, and the fallback archiving must not break

`GET /subscription-plans?status=` takes `Active` (the default), `Archived` or `All`. Anything else
is `400 subscription_plan_status_invalid`. `Draft` is not accepted and appears in no view.

`Active` keeps the organization-over-tenant resolution: an organization's own plan hides the
tenant's of the same code, because that is what subscribing resolves and a list showing both would
offer a choice it cannot honour. The archived views do **not** collapse by code — a replacement
sharing a code is usually the reason somebody is reading history.

Omitting the parameter is what every subscriber-facing caller does, which is what keeps archived
plans out of subscribe and change-plan selectors without those screens filtering anything.

The subtle part is on the selling side. `FindPlanByCodeAsync` filters to `Active` and resolves
organization-then-tenant, and it stays the only thing that decides what can be sold. The archived
catalogue is consulted **only after** it has returned nothing, and only to name the refusal.
Resolving both statuses together would let an organization's archived plan shadow the tenant's
active plan of the same code and refuse a sale that should have gone through. A plan belonging to
an organization the caller cannot see stays invisible, so the better message never becomes a way
to discover that a code exists somewhere.

### What gets recorded

Audit events carry nullable `AggregateType`/`AggregateId`/`AggregateCode`. Archiving a plan is the
first audited decision with no subscription in it, and writing the plan id into `SubscriptionId`
would have made the timeline query return a catalogue change as if it were something done to a
subscriber. Plan archiving writes `AggregateType = "Plan"`, `Operation = "PlanArchive"`,
`FromStatus`/`ToStatus`, and an `Outcome` of `Changed`, `AlreadyArchived`, `Conflict` or
`NotFound` — every attempt, including the ones that changed nothing, because a client retrying
against a plan it cannot see is exactly what somebody reading the trail later needs to see. The
audit write can never fail the archive it describes.

Responses carry `status`, `createdAtUtc` and `lastUpdatedAtUtc`.

### Naming a predecessor is a label, not a migration

`CreatePlanRequest.PredecessorPlanId` lets a new plan say which one it was created to replace.
It is set once, at creation, checked only for existing (and visible) at that moment, and never
read by anything that sells, prices, or migrates a subscriber. `GetPlanAsync`/`ListPlansAsync`
resolve the predecessor's display name so a caller can render a link without a second fetch; a
single-plan read also resolves the reverse — the plan, if any, that named *this* one as its
predecessor — via an unindexed scan scoped to the tenant, the same scale assumption
`ListPlansAsync` already makes for its own full scan. Naming one changes nothing about either
plan's `hasSubscribers`, editability, or purchasability: it is purely something a detail page can
show.

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

## Calendar-aligned billing

A monthly price can renew on the subscriber's anniversary — an August 25 signup renews
September 25 — or on the first of the calendar month. The choice is `billingAlignment` on the
price, snapshotted onto every subscription sold on it:

```json
{ "interval": "Month", "intervalCount": 1, "billingAlignment": "CalendarMonth" }
```

`Anniversary` is the default and the enum's zero, so every price and subscription written before
alignment existed deserializes to exactly the behaviour it was sold on.

**Only `Month` or `Year` with an `intervalCount` of 1 may be calendar-aligned.** A quarterly price
has no single "first" to renew on that is not also a choice of which month, so the combination is
refused at authoring time as `subscription_billing_alignment_invalid` rather than guessed at on an
invoice.

The two cadences align differently, and the difference is the whole of the yearly feature:

| | Anchors on | Opening period | Then |
|---|---|---|---|
| `Month` × 1 | the first of the month it starts in | the rest of that month, prorated | the 1st, every month |
| `Year` × 1 | the first of the month **after** | the rest of that month, prorated | the same 1st, every year |

A year anchored on the month it started in would end on the 1 August after a 25 August signup —
eleven months for a year's money — and no later boundary could correct it, because every one is
derived from the anchor.

### The opening period is a stub

The recurring schedule needs nothing new: a calendar-aligned `BillingSchedule` is an ordinary
monthly one anchored on the first at local midnight, and `BillingPeriodCalculator` derives every
later boundary from it as it always has. Only the first period is special, because it starts
mid-month.

A signup on August 25 gets `[August 25, September 1)` and pays **7/31** of the monthly amount —
the 25th through the 31st is seven calendar dates, counted inclusively, over the 31 the month
actually has. February uses 28 or 29 as appropriate.

Two consequences worth stating:

- **The time of day never enters into it.** Everyone who signs up on the 25th buys the same seven
  dates and pays the same fraction. Anything else would have a 23:59 signup paying for a day it
  had a minute of.
- **The subscriber's calendar decides, not the server's.** 31 August 23:00 UTC is already
  1 September in Zurich, and a Zurich subscriber signing up then gets a whole month rather than a
  one-day stub. A signup on the local first is a full period and is *not* reported as prorated.

### A yearly stub is priced from a linked monthly price

A monthly stub is a fraction of the very price being charged. A yearly one cannot be: a subscriber
joining on 25 August owes a week, and a week of an annual amount is not a quantity anybody can
charge. So a calendar-aligned **yearly** price must name the monthly price its opening period is a
fraction of, through `calendarStubBasePriceId`.

That link is required for `Year` × 1 calendar prices
(`subscription_calendar_stub_base_price_required`) and refused on every other price
(`subscription_calendar_stub_base_price_unexpected`), since nothing else would ever read it. The
referenced price is validated at authoring time — same plan, active, `Month` × 1, same currency,
same quantity item, same tax rate and mode — because every one of those, left to differ, produces
two figures a subscriber cannot reconcile and only discovers on an invoice.

The link prices the stub and nothing else. **The annual `unitAmountMinor` stays independently
authored**, because what a year costs is a commercial decision — an annual plan is usually not
twelve monthly ones — and deriving it would take that decision away from whoever is selling it.

Worked through for a plan at CHF 950 a month and CHF 11,400 a year, with 8% off for paying
annually, signing up 25 August:

| | Calculation | Amount |
|---|---|---|
| 25–31 August | `95000 × 7/31` = 21452, less 8% | **CHF 197.36** |
| 1 September | `1140000`, less 8% | **CHF 10,488.00** |
| 1 September next year | the same again | **CHF 10,488.00** |

The yearly price's own automatic discount and volume band apply to the stub as well as the year:
somebody who buys an 8%-off annual plan on the 25th is on that plan from the 25th, and charging
them undiscounted for the first week would be selling them the discount a week late.

A **promotional code is the exception — it applies to the year alone**, and is consumed once when
the year is settled. Spending a month of a customer's three-month promotion on a seven-day stub
would exchange a month of their discount for a week of it.

### When the year is collected

`calendarAnnualChargeTiming` decides that, and it is the only difference between the two calendar
yearly modes. Both come to the same money.

| | At checkout | On 1 September | Cancelling during the stub |
|---|---|---|---|
| `AtBoundary` (default) | the stub | the year is charged | access ends with the stub; the year is never charged |
| `AtCheckout` | the stub **and** the year | the year opens, nothing is charged | nothing is refunded; access runs to the end of the year |

An author choosing between these is choosing a refund policy as much as a collection date, which is
why the plan builder states both consequences rather than only the timing.

The field is required to be absent on every price that is not calendar-aligned yearly
(`subscription_calendar_annual_charge_timing_unexpected`) — anywhere else it would describe a choice
nothing acts on.

### The year in between

Between a mid-month signup and the first, the subscription carries a `PendingAnnualPeriod`: the
year's dates, its full financial breakdown, whether a promotion reduced it, and whether it has
already been paid for. Every figure is frozen when the checkout is created and **none is
recalculated at the boundary** — that boundary is a month later, and a charge that re-derived its
own amount could take a different sum than the one the subscriber agreed to.

The boundary charge and the period it opens are written in one transition, so opening the year and
forgetting that it was pending cannot come apart; a boundary that did the first and not the second
would find the year again on the next sweep and charge for it twice. A declined boundary charge
leaves the year pending and enters ordinary dunning, so the retry still owes exactly the frozen
amount.

**An unpaid year still refuses a plan or quantity change**, with
`subscription_initial_annual_period_unpaid`. Repricing before the opening charge has cleared would
have to silently discard a charge about to be collected, and that is not something a caller can be
told about after the fact. The wait is at most a month, and a downgrade or a decrease can still be
scheduled for the boundary in the meantime.

**A prepaid year no longer refuses one outright.** A change that keeps the subscriber's cadence and
calendar boundary — a compatible plan upgrade, or any quantity increase, since neither moves the
price — settles the stub's remaining days and the paid year together in one immediate charge; see
[Opening-stub upgrades](#opening-stub-upgrades) below. A change that would re-cadence a paid year,
or a genuine downgrade in disguise, still waits for the boundary exactly as an unpaid stub does —
`subscription_initial_annual_period_prepaid` is retired along with the blanket refusal it named.

The monthly amount and price id are **snapshotted onto the subscription**, so the stub is priced
without ever reading the monthly price again — not at checkout, not at renewal, not by a recovery
sweep. The stub is charged at checkout and the annual period a month later, so a live read would
let somebody editing the monthly price in between change what an annual subscriber already agreed
to.

> A yearly snapshot that carries no monthly basis cannot price a stub, and falls back to an
> ordinary anniversary year rather than prorating the annual amount by days. That fallback is
> deliberate: the alternative bills a week at roughly a twelfth of what it is worth, which is the
> one failure mode worth failing closed against.

Invoices follow the charges: `AtBoundary` produces a stub invoice at checkout and a separate annual
invoice at the boundary, `AtCheckout` a single invoice covering both. Line-level breakdown of that
combined invoice, and persisted invoice snapshots behind the history and PDF endpoints, are not
built yet — invoice history is still derived from settled payments, and begins at the first
renewal.

### The first charge is frozen at checkout creation

`InitialChargeAmountMinor`, `InitialChargeProrated`, `InitialChargeDiscountApplied`,
`ProrationDays` and `ProrationTotalDays` are written when the subscription is built and are never
recalculated. A checkout paid the following morning, resumed next week, or recovered by the
activation sweep settles the figure the customer was quoted — a stub priced by the day would
otherwise shrink underneath somebody who left the page open overnight.

`InitialChargeDiscountApplied` is frozen for the same reason the amount is, and it is the field
activation reads to decide whether the stub spent a discount period. Whether a promotion applies
depends on the clock, so a promotion that lapsed between the charge being raised and the webhook
arriving would otherwise look inactive at activation while the money already taken was reduced by
it — and the subscriber would get one more discounted renewal than they paid for. **Activation
never reprices the first charge.**

A **card-free trial is the exception**: it charges nothing at signup, and what its first paid
period will cost depends on when the trial ends. All of these fields are therefore left unset at
signup and written atomically when that first paid period is actually created — filling them in
from the signup date would record a fraction the eventual charge contradicts.

They are exposed on the subscription response, and kept after activation for tracing:

```json
{
  "billingAlignment": "CalendarMonth",
  "initialChargeAmountMinor": 3274,
  "initialChargeProrated": true,
  "prorationDays": 7,
  "prorationTotalDays": 31
}
```

`recurringAmountMinor` is unaffected throughout — it is what the next *full* month costs, which is
a different question from what the opening stub cost.

### The purchase preview is the same build, stopped one step short

`POST /api/subscriptions/preview` answers "what would this cost right now" without creating
anything. It is not a second implementation of the pricing — `SubscriptionCreationService` splits
its old `CreateAsync` into a shared `BuildSubscriptionAsync` that resolves the plan, price and
discount, builds the schedules, and runs `ApplyPeriods` to freeze the opening charge onto an
in-memory subscription; `CreateAsync` then persists it, and `PreviewAsync` reads the same fields
back out and never writes. `SubscriptionAmountCalculator.InitialChargeAmountMinor` — the exact
expression `SubscriptionCheckoutService` charges — is what both the confirm and the preview call,
so the two cannot quote a different figure from the same inputs.

Only one write is skipped on a preview: `IBillingAccountRepository.GetOrCreateAsync`, which
inserts a durable billing account nobody has confirmed subscribing yet. An unsaved stand-in serves
just as well — its id plays no part in the price, only in the record `CreateAsync` would go on to
store.

A condition that would refuse the confirm is reported as a **blocker** rather than a failure, so a
client sees the price and the obstacle together: an incomplete billing profile, or an existing live
or incomplete subscription for the organization (read via `GetLiveAsync` and `GetIncompleteAsync`
— the same two states the reservation index refuses a second insert for). A genuine input problem
— an unknown plan, price or discount code — still fails with the same code the confirm would.

`quoteValidUntilUtc` names the boundary rather than leaving the client to guess one: proration is
quantized per calendar day, so a quote is only exact until the next local midnight in the
request's own time zone — not until the period boundary, which can be weeks away. Null when
nothing is prorated, because then no boundary changes the answer.

### What the fraction applies to, and in what order

The gross is scaled once, at the front, and everything downstream applies to a prorated gross:

1. Gross from the price and its quantities.
2. **Prorate by covered calendar days**, rounded to the nearest minor unit, halves away from zero.
3. Built-in reductions — the price's automatic discount and the volume band the quantity selects —
   combined by the price's `QuantityDiscountCombination`.
4. Promotional code, settled against the built-in reduction by the plan's
   `QuantityDiscountCombinationPolicy`. A *fixed* code is prorated by the same day fraction; a
   percentage needs no scaling, since it is already a percentage of a scaled amount.
5. Tax, on the discounted amount, at the price's own rate and mode.
6. Banked credit, last of all — it pays the bill rather than changing what the bill was for.

Steps 3 and 4 are where proration has to happen *first* rather than last. A discount worked out
against a whole month and then subtracted from a fraction of one is not a smaller discount, it is a
larger one — 8% of a full month against a seven-day stub would take a third of the stub.

One subtlety inside step 3: `BuiltInDiscountCalculator` uses a volume band's resolved *money*
verbatim when the band wins, deliberately, so plans that had bands before automatic discounts
existed price them to the same minor unit. That figure is a whole month's, so a prorated period
re-expresses the band's *rate* against the prorated gross before handing it over. A whole period
passes the band through untouched, which is every anniversary subscription and every renewal after
an opening stub.

A fixed discount left whole would take a full month's reduction off a quarter of a month's charge,
and a large enough one would make the stub free.

A successfully paid stub **counts as one period** against a limited-duration promotional discount:
it is a period the subscriber was charged for, and three months of "20% off" that skipped the stub
would run to four bills. This applies to stubs only — an anniversary first period has never counted
here, and changing that would shorten every existing plan's discount for reasons unrelated to
calendar billing.

### Metering is left alone

The usage schedule is never realigned. Metering keeps the plan's own independent cadence, the
allowance stays whole for the stub, and nothing is reset or forcibly rolled over on the first —
an allowance is capacity for a period, not money to be prorated.

### Trial duration

A plan authors its trial length as one of three kinds (`TrialDurationKind`), plus a count where
the kind needs one:

| Kind | Count | Ends at |
| --- | --- | --- |
| `Days` | 1-365 | `count × 24 hours` after signup — a fixed span, never converted through a time zone. |
| `EndOfCalendarMonth` | none | Local midnight on the first day of the month after signup. |
| `AnniversaryMonths` | 1-12 | The same local wall-clock time, `count` months later, clamped to the target month's last day when signup's day-of-month does not exist there (31 January + 1 month lands on 28 or 29 February). |

The legacy `TrialDays` field on a plan is still accepted and is exactly `Days` with that count —
every plan authored before duration kinds existed keeps behaving identically, with no migration.
A request may set the legacy field or the current pair, never both.

`EndOfCalendarMonth` and `AnniversaryMonths` are resolved in the **subscription's own time zone**
(`BillingLocalTime`, the same DST-gap-and-ambiguity handling every other billing boundary in this
module uses), then converted to UTC and frozen. `Trial.EndsAtUtc` is an **exclusive** boundary —
a trial that resolves to local midnight on 1 September has run *through* 31 August, not into it,
even though the instant itself is timestamped 1 September. A UI showing that boundary to a
subscriber should describe it as "through August 31," not "ends September 1," which reads as one
day later than it is.

Because `EndOfCalendarMonth` is anchored to the calendar rather than a fixed span, a signup late
in the month gets a short trial — 31 August grants only until 1 September, by design; nothing
tops it up to a minimum length.

The resolved kind, count, start and end are all frozen onto `TrialTerms` at creation and never
recomputed. Editing a plan's trial rule afterward changes nothing for a subscriber already on it —
only a new signup sees the new rule.

### Trials

- A **payment-free trial** ending mid-month charges a stub from the local trial-end date to the
  next first. The period charged is the one the **trial ended in**, resolved from `Trial.EndsAtUtc`
  and never from the sweep's own clock. A conversion discovered after the next month boundary — a
  trial ending 20 August that nothing picked up until 2 September — therefore still bills the
  12/31 August stub it owes, keys it to August, advances to 1 September and leaves the
  subscription due again, so the next pass raises September as its own separate charge. Anchoring
  on the clock instead would silently write off the days in between.
- A trial ending **on the first** starts with a full period — a month for a monthly price, a year
  for a yearly one.
- A **payment-required trial** is charged up front at checkout, so its first fee uses the calendar
  stub exactly as an ordinary signup does.

This holds the same way regardless of which `TrialDurationKind` produced `Trial.EndsAtUtc`:

- 25 August signup, one `AnniversaryMonths` trial → ends 25 September → first charge 25 September.
- 25 August signup, `EndOfCalendarMonth` trial → ends 1 September → first charge 1 September.
- A calendar-aligned price whose next boundary is 25 September, converting from any trial ending
  before then, is charged its 25 September-1 October stub immediately and renews on 1 October —
  the trial only decided *when* the first charge happens, not which calendar boundaries the price
  itself renews on.

### Plan changes

Moving onto a calendar-aligned price installs that price's boundaries there and then, not at some
later renewal. Two different prorations meet, and deliberately are not the same kind:

- What the subscriber has left on the plan they are leaving is time they paid for, so it is
  credited **by elapsed time**, through the existing proration and credit logic.
- What they are buying is a calendar stub, so it is priced **by calendar dates** — the same 7/31 a
  fresh signup that day would pay.

This holds for a whole target month as much as for a stub. A change landing on the first is priced
`30/30` rather than "no fraction given", because the latter means "scale by elapsed clock time",
and that would charge a subscriber who moved at noon less than one who signed up fresh at noon for
the identical month. Calendar dates decide; the time of day is not one of them.

A change onto a calendar-aligned **yearly** price settles only the target stub immediately, priced
from that price's monthly basis exactly as a fresh signup would be. The annual cycle then opens on
the first like any other calendar-aligned year.

A positive difference is charged immediately; a negative one costs nothing and creates no new
credit — the same credit-never-banks clamp every settlement in this module applies. The target
schedule is installed atomically with the settled plan change, and the outgoing usage period is
closed and rated through the existing safe plan-change flow rather than being reset or discarded.

### What is unchanged

Cancellation, dunning, renewal recovery, idempotency and pending-checkout recovery all continue to
key on the frozen period boundary, so a failed renewal and its retry land on the same period and
raise no second charge. The first-period checkout still produces no Stripe invoice — invoice
history begins at the first renewal.

**Alignment is chosen when a price is created and cannot be changed afterwards.** Prices are
otherwise immutable in their commercial terms, and tax metadata is the one existing exception; an
alignment editor would need the same CAS-protected, future-snapshots-only treatment and does not
exist yet. To move a plan onto calendar billing, add a new price and retire the old one — existing
subscribers keep the terms they were sold on either way, since a subscription bills from its own
snapshot and is never migrated automatically.

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

- **Stripe → `StripeInvoiceBillingGateway`.** Raises a standalone Stripe Invoice per attempt — the
  invoice itself (`auto_advance=false`, so Stripe runs no retry schedule of its own), its line
  item, finalize, pay, and a void on decline. Order matters: the line names the invoice it belongs
  to, because recent Stripe API versions default `pending_invoice_items_behavior` to `exclude` and
  a line left pending is simply omitted — the invoice then finalizes owing nothing and reports
  itself paid. `auto_advance=false` withholds Stripe's retries but not collection, so a
  `charge_automatically` invoice is charged at finalization and the pay call is often redundant;
  the gateway reads each step's status instead of assuming where payment happens, and refuses to
  credit a renewal whose finalized invoice does not owe the amount charged. **No Stripe Subscription
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

A discount can be redeemed only once `DiscountTerms.StartsAtUtc` has been reached, and reduces a
renewal only while `DiscountTerms.DurationPeriods` and `ExpiresAtUtc` still
allow it; `SubscriptionDetail.DiscountPeriodsApplied` tracks how many periods it has already
reduced, so an expired discount's absence is detected without re-deriving history from past
charges.

`Subscription:DunningMaxAttempts` (default 4, including the first decline) and
`Subscription:DunningRetryIntervalHours` (default 24, a fixed interval rather than exponential
backoff — this is a business cadence for asking a customer to fix a card, not load-shedding
against a failing dependency) govern the cycle.

## What a settled period leaves behind

Every settled period is recorded as a `PaymentDetail` with `PaymentFlow = SUBSCRIPTION_INVOICE`,
already `CAPTURED` — an invoice paid at finalization has no authorize-then-capture step left to
drive. Renewals therefore appear wherever payments appear, and
`SubscriptionDetail.LastRenewalPaymentDetailId` names a real payment.

Two organizations are recorded, and the distinction matters:

| Field | Holds | Used for |
| --- | --- | --- |
| `OrganizationId` | the merchant's scope | resolving the provider and the card, as the charge did |
| `CustomerOrganizationId` | the subscriber | attributing the revenue, and authorizing invoice reads |

The write is best effort. By that point the money has moved, so a bookkeeping failure logs an error
and still reports the renewal paid — failing instead would have the next dunning attempt charge the
customer a second time.

`GET /api/subscriptions/invoices/{paymentId}/pdf` returns that period's invoice as a PDF, scoped to
the caller's organization (`?organizationId=` honored only for the console, as everywhere else). The
`paymentId` is the payment above, never the provider's invoice id.

`GET /api/subscriptions/invoices?pageSize=25&after=...` lists the same organization's settled
subscription invoices newest first. Each item carries the payment id, subscription, invoice type
(`Renewal`, `PlanChange`, or `Usage`) and applicable period parsed from the stable order id, amount,
refund total, status, and an authenticated `downloadUrl`
pointing at the PDF endpoint above. Pagination is cursor based; cursors are bound to the resolved
organization and cannot be replayed to move the query into another subscriber's history.

The bytes are proxied rather than the provider's own link returned. A Stripe `invoice_pdf` URL
carries no authentication and does not expire, so handing one out grants permanent access to a
billing document and puts it beyond this module's reach. For the same reason the link is read fresh
from the provider per request instead of stored — nothing in the database should be a standing key to
a customer's invoice. `StripeFileUrl` confirms the link is Stripe-hosted before the credentialed
fetch follows it, since a URL taken from a response body is still input.

A payment with no `CustomerOrganizationId` — one recorded before the subscriber was captured — is
refused rather than shown to whoever asks. Absent, another organization's, and not-a-subscription
all return the same not-found, so the refusal cannot be used to enumerate a tenant's payments.

## Automatic price discounts

A price can reduce itself. `PriceSnapshot.AutomaticDiscountBasisPoints` is a percentage taken off
every charge that price produces, with no code redeemed and no subscriber action — the mechanism
behind "8% off if you pay yearly".

It is on the **price**, not the plan, because the offer is cadence-specific: the yearly price
carries it and the monthly price beside it under the same plan does not. Two prices, one plan, one
product. A plan sold in two currencies has the same freedom, and neither case needs a second plan
that somebody would have to be moved between.

Three reductions can therefore meet on one charge, and there are two combinations because they
answer two different questions:

1. `BuiltInDiscountCalculator` settles the two the **merchant** authored — the price's automatic
   discount and the volume band the quantity selects — the way
   `PriceSnapshot.QuantityDiscountCombination` says. `BestDiscount` takes whichever reduces the
   charge by more money and only that one; `Additive` adds the two rates and applies the sum once,
   so 8% plus 5% is 13% rather than the 12.6% sequential application would give. Capped at 100%,
   because two generous rates must not arrive at a negative charge.
2. `PlanSnapshot.QuantityDiscountCombinationPolicy` then settles what a **redeemed code** adds to
   that result, exactly as it already settled band-versus-code. `QuantityOnly` now means "built-in
   discounts only"; its stored wire value is unchanged, so an existing plan means what it always
   did.

Collapsing the two into one policy would make a cadence discount negotiate with a coupon, which is
not a question anybody authoring a catalogue is asking.

`PeriodCharge` reports `GrossAmountMinor`, `BuiltInDiscountMinor` and `PromotionalDiscountMinor`
alongside the amount, so a subscription response, a quantity preview and an invoice can say why the
figure is what it is rather than leaving a client to reverse-engineer it from a total. A **settlement**
— a plan change or a quantity increase — cannot be described that way: its amount is the difference
between two prorated periods, so `SubscriptionProrationCalculator` returns a `ProrationBreakdown`
carrying both sides (each with its own gross, discounts, tax, period total and prorated value), the
credit consumed and the net. It is snapshotted onto the `SettlementReservation` when the change is
quoted — recomputing it at settlement time would price it at a different instant, possibly against an
edited catalogue — travels on the charge request, and is stored on `PaymentDetail.SubscriptionSettlement`
for invoice history. The flat fields and the settlement are alternatives, never both. A promotion
that lost is still not consumed — losing to an automatic discount counts as losing.

Discounts **truncate** to the minor unit, matching `QuantityDiscountCalculator`'s existing bands
exactly. Note which way that leans: truncating a reduction makes the reduction *smaller*, so it
favours the merchant by up to one minor unit — 5% of 199 takes off 9 rather than 10. It is kept
because a plan already charging a 5% band must not start charging it differently, not because it is
the generous direction. A price with no automatic discount goes through `BuiltInDiscountCalculator`
and comes out with its band's own arithmetic untouched, to the minor unit — which is the state every
stored price is in.

Both fields are snapshotted at signup and at plan change, like the amount and the tax:
`PUT /api/subscription-plans/prices/{priceId}/discount` reaches future subscriptions and future
moves onto the price, and nobody already on it is repriced either way. Metered overage is charged
by the price the subscription was sold on, so the automatic discount reaches an overage invoice too
(recorded on `SubscriptionUsageInvoice.DiscountAmountMinor`), before its tax. A volume band does not
— it prices units of a quantity item and a meter has none — and a promotional code still does not
reach usage invoices at all.

## Tax

`PriceSnapshot.TaxRateBasisPoints` and `PriceSnapshot.TaxMode` — manual, not jurisdiction-derived.
Whoever authors a price sets its tax rate the same way they already set its currency; there is no
address collection, no jurisdiction detection, and no external tax service behind it. That is a deliberate build-now
trade-off: it taxes every charge path correctly today — first charge, renewal, plan-change
proration and usage overage all already share `SubscriptionAmountCalculator`/
`SubscriptionProrationCalculator`, so one pipeline stage covers all four — at the cost of not
automatically knowing *which* rate applies to *which* customer. The person authoring the price
still has to know that.

A price says how much tax and **which of two things that means**. Exclusive, the rate is added to
the configured amount; inclusive, the configured amount is what the customer pays and the tax is
found inside it. The same "CHF 145.00 at 7.7%" is CHF 156.17 under the first and CHF 145.00 under
the second, and no amount of inspection of the number tells you which the author meant — so a
positive rate without a mode is refused at authoring time
(`subscription_price_tax_mode_required`). Both are snapshotted onto the subscription, so editing a
catalogue price's tax never reprices anybody already subscribed.

Prices authored before modes existed carry a rate and no mode. Those read back as **exclusive**,
because that is how every subscription sold on one has been charged; reading them any other way
would quietly cut live revenue by the tax. `TaxMode.Exclusive` is zero for exactly this reason —
the absent value has to deserialize to the behaviour already in force.

One place does the split: `SubscriptionAmountCalculator.TaxBreakdownFor`, returning net, tax and
total for a discounted amount. The renewal, the first charge, quantity previews, proration and
usage invoices all call it, which is what stops five code paths disagreeing about what 7.7% of
CHF 145.00 is.

**Explicit tax modes round to the nearest minor unit, halves away from zero.** For example, 7.7%
of CHF 145.00 is CHF 11.17. Legacy subscription snapshots that predate tax modes keep the old
exclusive, integer-truncation calculation (CHF 11.16 in that example), so deploying this feature
cannot silently alter an existing subscriber's charge. Choosing a mode on a new or updated
catalogue price opts future subscriptions into the rounded calculation.

The pipeline is **gross → built-in discount → promotional discount → tax → credit**, with the two
discount stages as described under [automatic price discounts](#automatic-price-discounts). Tax is computed on the *discounted* amount,
not gross — the same base the customer is actually being asked to pay. A banked credit is then
consumed against the tax-inclusive total: a credit offsets what the subscriber owes including
tax, it does not shrink the taxable base. A mid-period plan change taxes each side of the
comparison at *that side's own* price's rate before netting them, so a change between two
differently-taxed prices is still correct. A usage invoice taxes the aggregate total once, after
every meter's line is summed — the same "one charge, not one per meter" scope usage invoices
already keep for the charge itself, not a second, narrower exception to it.

The gateways charge one amount, with tax already folded into it. `SubscriptionChargeRequest` also
carries the split — net, tax, credit and rate — but only so an invoice can *show* it: this module stays
authoritative, and no gateway recalculates anything. `StripeInvoiceBillingGateway` renders a
subtotal line, a tax line naming the rate, and—when applicable—a negative subscription-credit
line. Their sum must equal the provider charge; otherwise the invoice is voided rather than
publishing a financially inconsistent document.

A future move to Stripe's own automatic tax would still be a real migration rather than an
extension: that model computes tax *outside* this module against a real customer address and expects
the gateway to read it back from the provider, the opposite of folding it in beforehand.

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
the subscription untouched; no partial change is ever written.

**A downgrade is never charged and never refunded — and it does not take effect today.**
`PlanChangeClassifier` reads the settlement before any credit pays for it: worth more than what it
replaces means immediate, worth the same or less means it waits. A change that waits is held as
`SubscriptionDetail.PendingPlanChange`, frozen with its target plan, price, quantities and both
schedules derived from the instant it becomes effective, and installed by the renewal at that
boundary in the single compare-and-set that advances the period. The subscriber keeps what they
paid for until then.

Two rules sit above that arithmetic. A trial is always immediate — it has paid for nothing, so
there is no paid period to protect. And a paid annual term being re-cadenced always waits, whether
it is an ordinary running year read from the price's own cadence, or a calendar-aligned opening
stub whose year is already prepaid: annual → monthly tends to settle *positive*, because a month
costs more than the remaining slice of a discounted year, so charging it now would bill the same
weeks twice. A prepaid stub that keeps its cadence is a third case, settled immediately by its own
arithmetic rather than by this one — see [Opening-stub upgrades](#opening-stub-upgrades) below.

**Nothing creates credit.** `SubscriptionProrationCalculator` clamps the balance so it can only
fall — credit already held is still spent against an immediate upgrade, and any remainder still
persists, but a settlement worth less than what it replaced hands nothing back. **There is no
refund path.** A credit is only ever applied to a future charge; it is never paid out, and nothing
in this module ever produces a negative amount.

**One pending commercial change at a time.** A booked plan change refuses a quantity change and
vice versa, enforced both by name in each service and by a filter on the write for the case where
two callers pass that check at once. `DELETE /api/subscriptions/{id}/plan/pending` withdraws a
booking; scheduling and cancelling each write a durable audit entry naming the previous and target
plan and price, when it was asked for and when it lands. Neither publishes `PlanChanged` — nothing
has changed yet; the renewal that applies it does.

**A trial changes plan with no charge and no credit at all** — nothing has been paid for yet, so
there is nothing to prorate. The plan, price and quantity snapshot simply swap.

Two restrictions keep this a contained piece of work rather than a rewrite of the billing clock:

- **Same currency, same billing interval only.** A different interval would mean rebuilding
  `FeeSchedule` and the current period's boundaries mid-flight, which is a separately-tricky
  problem this does not attempt — refused as a validation error rather than attempted.
- **`Trialing` and `Active` only.** `PastDue`/`Unpaid` is refused as a conflict: a customer who
  owes money changing plans is a support decision, not something to automate.

A paid plan or quantity change is not exposed to the race a version-keyed write would be: the
charge is raised against a `SettlementReservation` written *before* the card is charged, and the
promotion that installs the change afterward is addressed by the reservation id rather than by the
version that might have moved underneath it. If a worker dies between the charge succeeding and
that promotion landing, `SubscriptionSettlementReservationProcessor.RecoverStaleAsync` replays the
identical reservation and installs the identical terms, keyed on the same idempotent charge — the
same shape of recovery the initial checkout's `SubscriptionActivationProcessor.RecoverStaleAsync`
provides for the analogous race there.

### Opening-stub upgrades

A calendar-aligned yearly subscriber who upgrades — or adds units — while still inside the opening
stub, after that year has already been paid for, is settling two things at once: the stub's own
remaining days, and the difference between what the paid year cost and what it costs on the new
terms. `SubscriptionProrationCalculator.CalculateOpeningStubUpgrade` prices both and settles them
together, in one immediate charge, rather than the blanket refusal this used to be.

The two sides are priced by different rules, each mirroring exactly how the stub and the year were
priced at signup:

- **The stub** is priced at its own monthly-equivalent stub-basis rate
  (`CalendarBillingAlignment.TryStubBasis`), for both the outgoing plan and the target — the same
  substitution [above](#a-yearly-stub-is-priced-from-a-linked-monthly-price) uses, so a week is
  never priced as a twelfth of an annual rate. The subscriber's promotional code is deliberately
  excluded on both sides: a code belongs to the year, never to the days before it.
- **The year** is priced at the target's full rate for the whole period, promotional code
  included — the frozen `PendingAnnualPeriod` supplies the outgoing side verbatim, since that is
  exactly what was already charged or promised and re-deriving it risks drifting from what the
  subscriber agreed to. The code is repriced at the discount-period index that bought the year
  being replaced, not at the subscription's current one — a prepaid year has already spent its
  promotional period, and repricing at the current index would treat a one-period promotion as
  exhausted and quote the replacement year undiscounted.

**Credit is spent once, against the combined total, never against either side alone** — the same
credit-never-banks clamp every other settlement in this module uses. A combined delta at or below
zero applies immediately at zero charge, exactly as an ordinary change reaching a cheaper volume
band already does. A change that would still be worth less than what it replaces, or that would
re-cadence the year, does not reach this path at all: `PlanChangeClassifier` and
`ChangesCadenceOrAlignment` route it to the ordinary scheduled path above instead.

**Quantity increases go through the identical calculation**, called with the subscriber's own plan
and price on both sides — a quantity change moves neither, so there is no cadence question to ask
first. A decrease is unaffected: it still schedules for the year's end exactly as before.

The invoice for a settled upgrade carries two labelled sections rather than one —
`SubscriptionSettlementBreakdown.Annual` nests the year's own breakdown beneath the stub's — with a
single combined credit-and-net total at the end, since credit was never spent against either
section in isolation. A preview taken during a prepaid stub is quoted through the identical
calculation the confirm would charge, so it never shows a price the confirm would not actually
collect.

The reservation this settlement takes carries a `ReplacementPendingAnnualPeriod` alongside the
ordinary plan or quantity payload, so a crash between the charge and the promotion still installs
the correct new annual figures — the settlement-reservation recovery sweep applies it the same way
the request path does, stamping the confirmed payment onto a copy of the reservation's replacement
year (`PendingAnnualPeriod.SettledBy`) rather than the reservation's own instance, so a replay after
a crash and a clean run install the identical reference.

### The preview is priced fresh, not frozen

`POST /api/subscriptions/{id}/plan/preview` answers what a plan change would cost or credit,
without applying anything. Unlike the purchase preview
(`SubscriptionCreationService.PreviewAsync`), nothing here is frozen ahead of time —
`ChangePlanAsync` has never worked that way: it calls `SubscriptionProrationCalculator.Calculate`
(or, during a prepaid opening stub, `CalculateOpeningStubUpgrade` — see above) fresh, immediately
before charging, every time it runs. So the preview makes the same promise this module's
quantity-change preview already makes (`SubscriptionQuantityChangeService`'s own
`PreviewAsync`/`ChangeAsync` share one `RunAsync`, and price fresh on both paths) — the same
math, evaluated a moment later — rather than a stronger one this service has never provided.

`SubscriptionPlanChangeService` splits `ResolveAsync(preview)` from pricing: everything through
building the target schedule is shared, but pricing itself is not — `ChargeAndApplyAsync` still
calls the calculator itself on the real path, and the preview calls it separately, because the
calculator is a pure function of already-resolved inputs and cannot diverge by being called
twice.

Two conditions are collected as blockers on a preview instead of failing it outright — an
incomplete billing profile, and no saved payment method for a genuine upgrade — because neither
changes what the change would cost, only whether the confirm can go through. Everything else,
including an unsurvivable discount, still fails the preview exactly as it fails the confirm: the
real change never charges a price with the discount silently dropped, so there is no honest
number to quote alongside that refusal. `SettlementReservation` and `PendingAnnualPeriod` are
checked only on the real change — a preview is read-only and does not need either clear to quote
a price, mirroring the quantity-change preview's own treatment of the same two conditions.

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

## Fractional quantities are opt-in per meter

A meter counts whole units unless it says otherwise. `PlanMeter.QuantityScale` is how many decimal
places it accepts — `0` by default, up to `6` — and it governs that meter's allowance, its
carry-forward cap, its rate-band bounds, a trial grant for it, and every quantity recorded against
it.

Opt-in rather than global, because the two live on the same plan. A storage meter genuinely holds
512.5 GB; a screening meter has no half. Before quantities were fractional, `{ "quantity": 0.5 }`
was refused for free — JSON cannot bind a fraction to a `long` — and a global widening would have
silently spent that guard, making a stray `1.3333` from a calling product recordable and billable.
A plan already in the database has no `QuantityScale` field, so it deserializes to `0` and **cannot**
behave differently from before.

Recording validates against the **subscription's own snapshot**, never the catalogue's current
terms — the same rule its allowance and its rating follow, so editing a plan cannot change what an
existing subscriber may report. Over the scale, or over `MeterQuantity.MaxMagnitude`, is
`400 subscription_usage_quantity_scale_invalid`.

> **A carry-forward cap used to be dropped on the way in.** It was validated as mandatory, reported
> as `null` by every response, and read as "no cap at all" by `MeterAllowance.CarriedIn` — because
> nothing ever copied it from the request to the stored plan, or from the plan into the
> subscription's snapshot. A dormant subscription therefore banked allowance forever, which is the
> outcome requiring the cap is supposed to prevent. Both copies are now made. Unrelated to
> fractional quantities; found because these are the initializers the scale had to be added to, and
> invisible to the suite because every test set the field directly on a snapshot.

### Decimal, never double

Quantities are `decimal`, stored as BSON `Decimal128`. A reversal has to cancel the entry it
compensates to the last place; binary floating point cannot promise that, and the residue would sit
in the balance for the rest of the period and be billed.

**No data migration was needed, and this is why:** the driver's default `decimal` serializer reads
`Int64` and `Int32` back as decimals, so every counter and ledger row already written loads
unchanged, and the next `$inc` promotes the field in place. That mattered because the alternative —
fixed-point scaled integers — would have required rewriting the append-only ledger, which is the
authority every past invoice was computed from. `SubscriptionEntitySerializationTests` pins both
halves: the representation on write, and the tolerance on read.

`AppliedRecordCount` stays a `long`. It counts ledger rows, not units.

### Where a fractional charge becomes money

Half a unit at a rate of three minor units costs one and a half of them, and no invoice has a half
cent. The policy is **exact through the tiers, rounded once per meter**, half away from zero, in
`MeterQuantity.ToMinorUnits`:

| | |
|---|---|
| Tier arithmetic | exact decimal, no rounding |
| A band's reported amount | exact, and so possibly fractional |
| A meter's total | rounded once, to whole minor units |
| Everything downstream — aggregate, discount, tax | integral, exactly as before |

Rounding once per meter is what makes re-banding a rate table without changing any of its prices
leave the bill alone. Rounding each band and summing would let the *arrangement* of the table decide
the total: three bands at one rate would round three times where one band rounds once. It also keeps
a band breakdown adding up to the figure that was rounded, which a per-band rounding would not.

The preview needed no rounding rule of its own. It already defines the additional charge as
`Difference(projected, current)` of two fully rated totals, so parity survives by construction —
see [Metered overage preview](#metered-overage-preview).

Away from zero rather than to even, so a reversal of a charge that rounded up reverses the whole of
what was charged. Banker's rounding would leave a minor unit behind on half the reversals, and the
customer would have paid it.

### Bands are closed above and open below

`(previousBound, UpToQuantity]`, so a fractional overage lands in exactly one band. A band's
reported first quantity is the smallest one its meter can distinguish above that open bound — which
at scale `0` is the whole unit the band has always started at. A tier with `UpToQuantity = 400`
still reports units 1 through 400 on a whole-unit meter and the next band still starts at 401; a
three-place meter reports `400.001`, because `401` would leave everything between undescribed.

> **`UsageResponse` may now carry a non-integral `used`, `remaining`, `included` and `overage`,**
> and so may the plan and preview responses. A consumer parsing those into an integer breaks. There
> is no way to version a number's domain, so this is a breaking change to those fields for any
> caller that meets a meter with a scale above zero. Browsers should treat them as display-only and
> never sum them: JavaScript numbers are doubles, and client-side arithmetic on a billing quantity
> would drift from the server's exact decimal.

## The current-usage projection

`SubscriptionUsageCurrent`, one document per subscription, meter and usage period, in the **tenant's
own database** — not `BlocksRootDb`. Its `_id` is the counter's own composed id,
`{subscriptionId}:{meterKey}:{periodKey}`, so a projection addresses its source without a lookup.

It exists so "how much is left?" is one indexed read. The authoritative answer needs a subscription
resolved, its meters walked and a counter read per meter; a consumer that only wants to draw a usage
bar or skip a request that is going to be refused should not pay for that.

### It is not an authority, and it cannot become one

Only `POST /api/subscription-usage` with `enforce` can claim capacity, because only the counter's
atomic increment settles two callers wanting the same last unit. Two callers reading this collection
at the same instant will both be told the same figure remains, and they cannot both have it. That is
not a defect to be fixed here — it is why the counter exists.

Everything in the document is **derived**. `used`, `remaining` and `overage` are copied from the
counter result the authoritative write produced. Nothing increments this document. A projection with
its own arithmetic would be a second set of billing rules, and the two would disagree exactly when it
mattered.

### Published synchronously, ordered by version

The authoritative sequence is unchanged: append the ledger entry, update the counter atomically,
apply any reversal, and **then** publish. Publishing last is what guarantees a reader never sees the
momentary exceeded balance that an enforced refusal passes through on its way to being undone — a
refusal publishes the post-reversal figure, which is the same one the caller is told.

Writes are conditional on a **pair** of versions, and both halves are load-bearing.

`counterVersion` is the counter's `AppliedRecordCount`. It only ever rises — `ApplyDeltaAsync`
increments it once per ledger entry, `TryRepairCounterAsync` writes only a strictly greater value —
so the **highest version wins, not the last writer**, and a request delayed between updating its
counter and publishing cannot overwrite a newer balance.

`subscriptionVersion` is `SubscriptionDetail.Version`, which every mutating write in
`SubscriptionRepository` increments. It exists because the counter version orders *usage* and nothing
else: a plan change, a quantity change, a cancellation or a status transition alters what the
projection **says** without recording any usage, so the counter version is unchanged. Ordered on the
counter version alone, a republish carrying the new allowance compares equal and is refused as stale
— and the projection would advertise the old terms **indefinitely**, until somebody happened to
record usage against that meter. It is a tie-break rather than an alternative:

Crucially, **each version governs its own fields and neither replaces the whole document.** The
write is an aggregation pipeline:

| Field group | Moves when |
| --- | --- |
| `used`, `expiresAtUtc` | `counterVersion` is newer |
| `subscriptionStatus`, `planId`, `planCode`, `unitLabel`, `overageAllowed` | `subscriptionVersion` is newer |
| `included` | either version is newer — but a writer whose `subscriptionVersion` is **behind** the stored one may not touch it |
| `counterVersion`, `subscriptionVersion` | each becomes the **maximum** of stored and incoming |
| `remaining`, `overage` | recomputed from whichever `used` and `included` won |

`included` is the awkward one, and it is not plan metadata. `MeterAllowance.Effective` computes it
from the plan's terms **and** the counter's `LimitSnapshot` — the allowance frozen when the window
opened, which is where a carry-forward from the previous period lands. So the counter can change the
allowance with no plan change at all: a seed publishes the opening figure before any counter exists,
and the first recording opens the counter with a possibly different frozen snapshot. Owned by the
subscription version alone, that correction could only arrive with an unrelated plan edit. The guard
is what stops this reopening the regression below: a writer holding pre-plan-change terms still cannot
undo a newer plan's figure.

A single conditional replacement of the whole document cannot honour both, and got it wrong in both
directions. A cancellation publishing `(counter 10, subscription 6, Cancelled)` followed by a usage
request already in flight publishing `(counter 11, subscription 5, Active)` restored `Active` and
drove the stored subscription version **backwards** from 6 to 5 — leaving a cancelled subscription
advertising a live allowance. In the other order, a lifecycle refresh carrying `(10, 6)` was rejected
outright because its counter was not newer, so its metadata never landed at all.

Per-field, with a maximum on each version, every write is idempotent and order-independent: the
document converges on the newest of each kind of information whichever order the writers arrive in,
which is what makes this safe without a transaction.

`remaining` and `overage` are recomputed in a second pipeline stage rather than copied, because they
are pure functions of `included` and `used` — and after a merge those two can come from different
writers, so copying either would describe a balance that never existed. That is not the projection
doing billing arithmetic; it is the same one-line function the authoritative response uses, evaluated
where both inputs are final.

### The gap that cannot be closed, and what closes it anyway

There is no transaction across the counter write and the projection write. A process that dies
between them leaves the projection behind with nothing to announce it. Four things cover that:

| Path | Covers |
| --- | --- |
| Synchronous publish, retried briefly for transient Mongo errors | the ordinary case |
| `UsageProjectionRefresh` queue item, scheduled when a publish fails after the usage committed | a failure the request itself saw |
| Explicit announcement at plan change, quantity change and cancellation | a metadata change, which moves no counter and so is invisible to the sweep |
| Version-comparison sweep in `SubscriptionRepairAnnouncer` | a miss nothing announced — **both** versions, so it also catches a lost metadata announcement and reaches cancelled subscriptions the backfill's live roster excludes |
| **Backfill pass** over the tenant's live subscriptions | a window with no document at all |
| Projection read falls back to the counters, completely | anything still missing when a read arrives |

The lifecycle announcements are **best effort by design** — they route through
`TryScheduleAsync`, which swallows and logs, because a read model that could not be announced must
never fail a plan change or a cancellation the customer already has confirmation of. The sweep is what
makes that safe: it compares the projection's `subscriptionVersion` against
`SubscriptionDetail.Version`, so a lost announcement is found rather than merely hoped about. A
counter-only comparison would have missed every one of them, and would never have reached a cancelled
subscription at all.

The backfill is what makes direct access safe to enable. The sweep reads the projection collection, so
a document that was never written is invisible to it — a subscription predating this collection, a
seed that failed, a process that died before the first publish, a meter added to a plan afterwards.
The API can fall back to the counters, but **a consumer reading the collection directly cannot**: it
would simply see no meter. So the backfill enumerates the authoritative side instead, walking the
tenant's live subscriptions one bounded page per pass and publishing whatever is missing. It cycles
rather than migrating once, because a meter added tomorrow is a missing document tomorrow.

Its place in the roster lives in `UsageProjectionBackfillCursors`, **registered as a singleton**. That
is not incidental: the reconciler is scoped and the reconciliation service opens a fresh scope per
tenant sweep, so a cursor held on the reconciler was recreated empty every pass and the backfill
re-read page one forever — no tenant larger than one page ever had its later pages published. The
cursor is in memory, so a restart begins again from the start of the roster; that costs a repeat of
work which is idempotent and version-ordered by construction. With several replicas each walks the
roster independently rather than dividing it, which is slower to cover a large tenant than a durable
shared cursor would be, and still complete.

A failed publish **does not fail the request.** The response is the authoritative `200` with
`projection: "Pending"` on it, and a repair scheduled. Returning an error would let a read model veto
a committed billing write, and the usage is recorded either way — a caller that retried under a new
idempotency key because it read a `503` as "not recorded" would double-count.

Zero-usage documents are created on activation and when a period rolls over, so a consumer can
discover a subscription's meters and allowances before any usage exists — the difference, to a reader
that cannot see the plan, between "no usage yet" and "no such meter". Seeding never overwrites a
balance.

Rollover is the one that matters most and is easiest to get wrong. Crossing a period boundary
addresses a *different* counter id, so the new window starts with no projected document at all. The
API would fall back to the counters; **a direct consumer has no fallback**, so at one minute past
midnight it would see nothing for a periodic meter, or — worse, because it looks like an answer —
only the never-resetting ones.

So `CloseDuePeriodsAsync` returns a `UsagePeriodClosureOutcome` naming the subscriptions that
actually closed a window, and `UsagePeriodClosureWorkHandler` publishes exactly those. The refresh set
is the **committed outcome**, not a second guess at it.

That distinction is the whole point. Re-running the due query to find out who rolled is not
equivalent: it has its own batch size (`UsageRatingBatchSize` versus the projection's own), takes its
own `now`, and by then the clocks have advanced. It would name subscriptions that were not closed —
including one deferred by an outstanding usage claim, which rating skips — and miss ones that became
due in between. Equal default batch sizes do not make the two sets the same set.

A projection failure here **cannot fail the closure item.** The closure has committed by the time the
refresh runs, so letting the failure out would retry a rating pass because a derived read model could
not be written. The handler absorbs it, logs which subscriptions have an unpublished window, and
announces a repair for each; cancellation still propagates, because that is the worker shutting down
rather than a projection problem.

> Two earlier versions of this were wrong. The first hooked the handler behind the queue item naming a
> subscription — nothing calls `ScheduleUsagePeriodClosureAsync`, so every item comes from the repair
> sweep and names none, and the branch never ran. The second used a second due query, per above. And
> the test named for the no-retry guarantee asserted that the exception *was* thrown, documenting the
> bug as though it were the design. `UsageProjectionRolloverTests` now pins all three: restoring the
> old gate fails three cases, and removing the catch fails two.

### Reading it

`GET /api/subscription-usage/current` takes an optional `readMode`:

| `readMode` | Reads |
| --- | --- |
| omitted or `authoritative` | the counters. **The default**, so no existing caller's behaviour changes |
| `projection` | this collection, in one query, falling back to the counters if it cannot answer completely |

Anything else is `400 subscription_usage_read_mode_invalid` — refused rather than quietly defaulted,
because a caller that misspells `projection` and is served the counters would measure the wrong thing
and conclude the projection had no benefit.

A projection read answers **only if it holds every current window the plan defines.** If it holds
some but not all, the counters answer the whole request and a repair is scheduled — returning the
published subset would omit meters the plan defines, with nothing in the body to say so, and a caller
drawing a usage screen from it would show fewer meters than the subscription has. The two modes are
required to return equivalent data, and a subset is not equivalent.

The two kinds of fallback are reported separately, because they mean different things: nothing
published is a subscription the projection has never covered (a backfill matter), while some
published is a lost write for a subscription it does cover (worth looking at, and repaired).

Both modes return the identical `UsageResponse[]` body. How the read was served is reported in
headers, so opting into a mode never changes the shape a consumer parses:
`X-Usage-Read-Mode`, `X-Usage-Read-Source`, `X-Usage-Read-Fallback` (`None`, `ProjectionEmpty`,
`ProjectionPartial`), `X-Usage-Read-Duration-Ms`, `X-Usage-Read-Documents`, `X-Usage-Read-Stale`,
`X-Usage-Projection-Age-Seconds`.

The authoritative mode was also fixed while this was built: it read one counter per meter, and now
reads them in one batch. That batch is keyed by **composed id, not period key** — a `Never`-reset
capacity meter lives under `LIFETIME` while its periodic neighbours use the billing schedule's key,
so the meters of one subscription do not share a period, and a batch filtered by any single period
would have returned nothing for the others and reported them as unused.

### Watching it

Meter `Blocks.Subscription.UsageProjection`, exported through OTLP like
`Blocks.Subscription.BackgroundWork`.

| Instrument | Answers |
| --- | --- |
| `subscription.usage.read.duration` (histogram, by mode) | is the projection actually faster? p50/p95/p99 for both modes come out of one instrument, so they are directly comparable |
| `subscription.usage.read.count` (by requested and actual mode) | how much traffic each mode carries |
| `subscription.usage.read.fallback.count` (by reason) | how often the projection could not answer, and which kind |
| `subscription.usage.read.stale.count` | reads containing a document past the threshold |
| `subscription.usage.projection.age` (histogram) | how far behind the projection is when read |
| `subscription.usage.projection.version_lag` (histogram) | how far behind in ledger entries, measured by the sweep, which is the only place that reads both sides |
| `subscription.usage.projection.publish.duration` / `.count` | latency this adds to a customer-facing billing call, by outcome |
| `subscription.usage.projection.publish.failure.count` | publishes that left a projection behind and scheduled a repair |
| `subscription.usage.projection.repair.scheduled.count` / `.completed.count` (by source) | repair volume, by what noticed the miss |

The meter is registered with OpenTelemetry in **both** processes that record into it — the Api, where
reads and the synchronous publish happen, and the Worker, where the sweep and backfill run. Creating
instruments does not export them: an exporter observes only the meters it has been told to subscribe
to, so without `AddMeter` these would have been recorded and silently dropped. The Api had no metrics
pipeline at all before this, so one was added there; it reads its endpoint from the standard
`OTEL_EXPORTER_OTLP_*` environment, as the Worker's does, **which the Api deployment must supply.**

**No tenant, organization or subscription dimension on any of them** — a per-tenant label multiplies
every series by the tenant count, and there are thousands. Those identifiers go on the **logs and the
trace span** instead, where they are attached to one read: hashed `TenantHash`,
`OrganizationHash`, `SubscriptionHash`, plus `CorrelationId` and `TraceId`. So "a read was slow" can
be turned into "which customer saw it", which is the first question anybody asks.

Slow, stale and fallen-back reads are always logged. An ordinary read is sampled down to debug,
because this is one line per call of a dashboard endpoint.

### Reading it directly, from outside this service

Intended, and the reason it is a separate collection: a consumer can be granted read access to
exactly this and nothing else. Granting a reader `SubscriptionUsageCounters` would hand it the
enforcement authority for metered billing.

**The grant itself is not created by this repository.** A read-only Mongo role scoped to
`SubscriptionUsageCurrent` in the resolved tenant database has to be provisioned where database users
are managed. Nothing here can do it, and nothing here should be read as having done it.

A direct query must include the organization and the period boundaries. Both are indexed
(`ix_usage_current_org_subscription_status_period`); an unscoped query is a collection scan and a
cross-organization read of billing state.

Staleness is exposed rather than hidden: `counterVersion` and `updatedAtUtc` are on every document. A
projection is stale when its version is behind its counter, or when its age exceeds
`Subscription:UsageProjectionStalenessSeconds` (default 900). Age alone cannot tell a quiet meter
from a missed publish, which is why the read reports it and only the sweep — which reads both sides —
acts on it.

Retention follows the counter's: the same `Subscription:CounterRetentionDays`, so a projection never
outlives what it projects. A lifetime window is kept as long as the subscription, because its
allowance has no later window to move to.

## Usage rating

`PlanMeter.RateTables` existed from the first commit but priced nothing until now — usage was
recorded and enforced, never turned into a charge. `SubscriptionUsageRatingProcessor` closes that
gap on the usage clock's own schedule, independent of the fee renewal:

**Closing a period** (`CloseDuePeriodsAsync`, driven by `NextUsageBillingAtUtc` — set at creation
since Phase 1 but not read again until this) prices every counter's balance against the plan
snapshot's matching meter via `SubscriptionUsageRater`, and records a `SubscriptionUsageInvoice`
before advancing the period. Only the **overage** is priced — `usage − IncludedQuantity` — with
tier boundaries counted from the first overage unit, inclusive; `IncludedQuantity` stays the only
place a plan's free allowance lives, so a rate table never needs a zero-cost first tier to
represent it. A meter with no rate table in the subscription's own currency rates to zero rather
than blocking every other meter's charge over one misconfigured plan.

A sweep that missed several months (worker downtime) closes every intervening period, not just
the most recent one — the loop is capped at 24 iterations, the same defensive bound
`BillingPeriodCalculator` places on its own index correction.

**Charging an invoice** (`ChargeDueInvoicesAsync`) goes through the same `ISubscriptionBillingGateway`
a renewal and a plan change use — this module's fourth caller of that seam. The order id is
stable per period (the payment module allows only one recurring payment per order id, ever), but
the idempotency key carries the attempt number: reusing one key across retries would replay a
declined attempt's cached result forever rather than actually trying again, the same reasoning a
renewal's dunning retry already follows.

> **This is deliberately a second, independent invoice from the fee renewal.** A decline retries
> on its own bounded schedule (`Subscription:UsageRatingMaxAttempts`/`UsageRatingRetryHours`,
> separate settings from the fee-side dunning cycle) and is abandoned — never charged again, never
> retried further — once that runs out. It never touches the subscription's `Status`. A customer
> whose card is declined for last month's overage keeps whatever the fee renewal already paid for.

`SubscriptionUsageInvoice` is created *before* the charge is attempted, the same discipline
`SubscriptionPaymentLink` uses for the initial charge, so a crash mid-attempt is recoverable by
re-reading the same record. Its uniqueness index on `(TenantId, SubscriptionId, PeriodKey)` is the
double-billing guard.

**Known gaps, stated rather than built around:**

- **One aggregated charge per period, not one per meter.** `ISubscriptionBillingGateway.ChargeAsync`
  takes one amount and one description; per-meter line items are still recorded on the invoice
  itself for support traceability, but the actual charge is always the total.
- **A `Canceled` subscription's still-open final period is never rated.** An immediate
  cancellation clears `NextUsageBillingAtUtc` the moment entitlement stops, so any usage recorded
  in that unrated final stretch has no billing path today.

## Metered overage preview

```http
POST /api/subscription-usage/overage/preview
```

```json
{ "organizationId": null, "meterKey": "screening", "additionalQuantity": 100 }
```

Estimates what a hypothetical slice of additional metered usage would cost, using the active
subscription's own snapshotted terms and the same rating, discount and tax logic
`SubscriptionUsageRatingProcessor` uses to charge the final usage invoice —
`UsageChargeCalculator` and `SubscriptionUsageRater.OverageAllocations` are the two pieces shared
between them, so the two can never quietly drift apart. Subscription resolution and the platform
console's organization override follow the same rule `GET /subscription-usage/current` already
applies (see "Console organization override" above).

**Advisory, and read-only.** `SubscriptionUsageOveragePreviewService` takes only dependencies
capable of reading — no usage ledger, no invoice repository, no billing gateway, no outbox, no
audit trail — so there is nothing here that could write even by mistake. The response says so
explicitly: `writesUsage` and `chargesPayment` are always `false`, and
`finalChargeDependsOnActualPeriodEndUsage` is always `true` — usage recorded after
`calculatedAtUtc` can still change what period-end rating eventually charges.

The response reconciles three views of the period: `currentCharge` (rated from usage recorded so
far), `projectedPeriodCharge` (rated as if the additional quantity had already happened), and
`additionalCharge` — the **difference** of the two, never rated on its own. A tier boundary the
additional units cross, or a rounding step at the discount or tax boundary, can price the same
units differently depending on what came before them in the period; only the difference of two
fully rated totals is guaranteed to match what the period-end invoice would actually add.

Both `currentCharge` and `projectedPeriodCharge` are rated across **every** billable meter on the
subscription, not only the one named in the request — the worker totals overage across every meter
before it applies the automatic discount and tax once, across the whole invoice, and a preview that
discounted and taxed only the requested meter in isolation could disagree with that total at a
rounding boundary whenever another meter already carries overage. `additionalTierBreakdown` stays
scoped to the requested meter — it names which graduated tier bands the additional units fell into
and what each contributed, for display — it is informational only, since the authoritative
additional charge is the aggregate difference described above, not a sum of independently-taxed
bands.

Included quantity is resolved through `IMeterAllowanceResolver`, so a trial grant or a
carried-forward allowance changes the preview the same way it changes enforcement and the
entitlement read. Everything is read from the subscription's own snapshot — `Plan`, `Price`,
`UsageSchedule` — never from the mutable plan catalogue, for the same reason period-end rating
reads its snapshot rather than the live plan.

**Promotional discounts do not apply here, on purpose.** Metered overage has never been
discountable by a promotional code — only the price's own `AutomaticDiscountBasisPoints` reaches
it, exactly as in period-end rating — and the preview states that fact explicitly
(`discount.promotionalCodeApplied` is always `false`) rather than leaving a client to wonder
whether a code was simply overlooked.

Named failures rather than a misleading zero price: `subscription_not_found`,
`subscription_meter_not_found` (404); `subscription_usage_preview_invalid` (400, missing
`meterKey` or a non-positive `additionalQuantity`); `subscription_meter_overage_not_allowed`,
`subscription_meter_rate_unavailable` (409, a meter that refuses overage or has no rate table for
the subscription's currency); `subscription_schedule_unavailable` (503, the usage schedule could
not place "now" in a period).

### Overage terms on `GET /subscriptions/current`

```json
{
  "subscriptionId": "sub-123",
  "currencyCode": "CHF",
  "meters": [
    {
      "meterKey": "screening",
      "displayName": "Screenings",
      "unitLabel": "screening",
      "includedQuantity": 150,
      "resetPolicy": "Periodic",
      "quantityScale": 0,
      "carryForwardCap": null,
      "overageAllowed": true,
      "overagePricing": {
        "currencyCode": "CHF",
        "tiers": [
          { "upToQuantity": 100, "unitAmount": "1.00" },
          { "upToQuantity": null, "unitAmount": "0.80" }
        ]
      }
    }
  ]
}
```

`SubscriptionResponse.Meters` names the terms a subscriber actually bought, one entry per meter
`SubscriptionDetail.Plan.Meters` defines -- read from the subscription's own plan snapshot, the
same one everything else in this section reads from, never the mutable catalogue. `Meters` is
additive to the response and empty (never null) for a legacy subscription whose snapshot predates
metered usage.

`overagePricing` is `null` for two distinct reasons a client has to tell apart from
`overageAllowed` alone: overage is blocked outright (`overageAllowed: false`), or overage is
allowed but this plan defines no rate table for the subscription's own `CurrencyCode` (or a rate
table's amounts could not be converted -- see below). Either reading leaves `overageAllowed: true`
with `overagePricing: null`, which is why the field is reported separately rather than folded into
a single "priced or not" boolean.

**Major units, not minor.** Every other amount on `SubscriptionResponse` is a minor-unit `long`,
matching the entities and every other financial response in this module. `OverageTierResponse`
breaks that pattern on purpose: `unitAmount` is an invariant decimal string in major units --
`"1.00"` (CHF), `"100"` (JPY, no decimal places), `"0.100"` (KWD, three) -- because this is the one
place on the response meant for direct display rather than further arithmetic, and a minor-unit
figure would force every caller to duplicate the same currency-exponent lookup
`MinorUnitMajorAmountFormatter` already does once, from the payment module's own
`ICurrencyMinorUnitResolver` -- never a hardcoded assumption of two decimal places. If a rate
table names a currency the resolver can no longer convert, the whole tier list (not just the
offending tier) is reported as `overagePricing: null` rather than a partially-priced list mixed
with a fabricated conversion; the meter's other fields, and the rest of the response, are
unaffected -- the endpoint stays available and simply reports that meter's pricing as unavailable.

The preview endpoint above is unchanged by this: it keeps its exact minor-unit response, and
remains the only place to get an authoritative, rated quote for a specific quantity.

## Events

Appended in the same write as the state change that caused them, then published to
`blocks_subscription_lifecycle_topic` by a processor. MongoDB and the bus share no transaction,
so publishing inline would drop events precisely when something went wrong.

`SubscriptionCreated`, `SubscriptionTrialStarted`, `SubscriptionActivated`,
`SubscriptionActivationFailed`, `SubscriptionCancellationRequested`, `SubscriptionCanceled`,
`UsageThresholdReached`, `SubscriptionRenewed`, `SubscriptionRenewalFailed`,
`SubscriptionPastDue`, `SubscriptionUnpaid`, `SubscriptionPlanChanged`, `UsageRated`,
`UsageRatingFailed`.

The worker consumes `UsageThresholdReached`. It resolves the subscription's billing account and
queues a Blocks OS mail command to `blocks_email_listener` for `BillingEmail`, using purpose
`subscription_usage_threshold` and language `en-US`. The configured mail template can use these
subject/body context values: `DisplayName`, `PlanName`, `PlanCode`, `MeterKey`,
`ThresholdPercent`, `Balance`, and `Limit`. Blocks OS must have a template with that purpose for
the email to render and send.

Other lifecycle events remain facts for integrating products to consume; this repository does
not turn them into customer notifications.

Every event carries a correlation id, persisted at write time, because publication happens later
in another process. Without it the trace ends at the queue.

## Collecting a card without charging one

An opening amount of zero and a subscriber with no card on file used to be the same thing,
because the only way to hold a card was to charge it. `Plan.RequirePaymentMethodUpfront`
separates them.

The signup path forks three ways:

| Opening amount | Card required | What happens |
| --- | --- | --- |
| more than zero | — | the existing payment checkout |
| zero | no | activates immediately, no `checkoutUrl` |
| zero | yes | a **card-setup** checkout; `Incomplete` until the card is stored |

A card is required when the amount is zero and either the plan sets
`RequirePaymentMethodUpfront`, or the subscription starts on a trial whose
`TrialRequiresPaymentMethod` is set. Both together is the combination the setting was asked for:
genuinely free until the trial ends, with a card on file so the charge that ends it has something
to bill.

The setup is a Stripe Checkout session in `setup` mode — not a one-cent charge, which appears on
a statement and has to be refunded, and not a zero-value PaymentIntent, which Stripe rejects. The
SetupIntent it produces carries the off-session mandate the first renewal relies on.

It leaves a `PaymentDetail` behind, under `PaymentFlows.PaymentMethodSetup` with a zero amount.
That record exists because everything which tracks a hosted session already hangs off one: the
initiation lease, the redirect URL, the webhook route, the stored-card write. **It is not a
payment.** It is excluded from payment listings, from refunds and captures, and from invoice
history, and activation does not record it as the opening charge — `InitialPaymentDetailId` stays
null, because there is no charge and no invoice behind one.

Two things behave differently from a charge:

- **Failure is not fatal.** A declined charge ends the subscription; nothing was refused here, so
  it stays `Incomplete` and another attempt is free to succeed. The staleness sweep still expires
  it if nobody comes back.
- **An expired session is replaced.** A hosted session cannot be reopened and the provider would
  replay it under the key that opened it, so a retry mints a new one —
  `SubscriptionConstants.PaymentMethodSetupKeyFor` carries an attempt number, bumped by a
  compare-and-set so two tabs retrying at once produce one session. An expired *charge* is still
  a conflict: raising a second one is how the same money gets taken twice.

Cancelling while a setup is outstanding settles its link, so completing the card form afterwards
cannot start a subscription somebody has cancelled.

## Activation waits for the webhook

A subscription becomes active only when the payment carries both a confirming status **and**
`WebhookConfirmedAtUtc`. The shopper's return from checkout is not evidence: a redirect can be
replayed, forged, bookmarked, or lost when someone shuts the laptop.

This holds for a card setup too, on `setup_intent.succeeded` rather than a payment event. The
setup record settles to `Authorized` and never captures, which is what keeps every total that
sums captured money from picking it up.

Clients should therefore expect a brief `Incomplete` window after paying — the browser usually
comes back before the webhook lands.

Activation runs on the payment work tick, which every inbound webhook already dispatches, so it
happens within milliseconds. `SubscriptionReconciliationBackgroundService` is the safety net for
what no message carries: a compare-and-set lost to a worker that then crashed, a charge raised
but never recorded, a webhook that arrived during a restart.

It is a safety net in one direction only. The sweep **announces** work to the durable queue and never
executes it; the queue drainer is the only thing that runs subscription background work. See
`Scheduling/README.md` for why two executors was worth removing — briefly: one renewal charged twice.
`Subscription:SchedulerEnabled` no longer selects between them and is ignored.

### Which tenants the sweep covers

Discovered, not configured. `SubscriptionTenantDirectory` reads the platform's own tenant
registry — the `Tenants` collection in the root database, reached by connection string and
database name rather than by ambient tenant, which is what makes it readable from background work
that has no request to resolve one from.

The roster is asked for on **every pass** and cached for `TenantRefreshSeconds`. It is never
captured at startup: projects are created at any time and can subscribe immediately, so a list
read once is stale the moment the next one appears — and a tenant the sweep never visits is a
tenant whose renewals silently never happen.

Three rules follow from that, and each exists because its opposite fails quietly:

- **An empty roster is a quiet pass, never the end of the loop.** On a fresh environment nobody
  has signed up yet; that is not a misconfiguration, and the loop must still be running when the
  first tenant appears.
- **A failed read keeps the last known roster.** Sweeping nothing because the registry blinked
  would stop billing while the service went on looking healthy.
- **`TenantIds`, when set, overrides discovery entirely.** For pinning one tenant locally, and as
  an escape hatch if discovery itself is ever the problem.

The refresh interval is generous on purpose. Nothing time-critical waits on it: a subscription
activates from the payment webhook, which carries its own tenant and never consults the roster.
The sweep only matters at the first renewal, a whole billing period later.

## Settings

## Catalogue capabilities

Meters choose their reset behavior independently. `MeterResetPolicy.Periodic` is the backward-
compatible default and addresses a counter by the configured usage window. `Never` addresses the
stable `LIFETIME` counter instead, so persistent capacity such as storage remains consumed across
fee renewals and usage-window boundaries. Monthly rating only prices periodic counters.
Positive recordings consume lifetime capacity; negative recordings release it and are rejected
for periodic meters or when they would take the lifetime balance below zero.

Plans may declare one usage allowance cadence independently of their fee cadence through
`UsageInterval` and `UsageIntervalCount`. Both values are copied to `PlanSnapshot`; changing the
catalogue later cannot move an existing subscriber's reset window. Plan changes rebuild both the
fee and usage schedules from the change instant and permit monthly/annual moves when currency is
unchanged.

`DiscountTerms` snapshots the applicability lists alongside the amounts, and
`SubscriptionPlanChangeService` re-asks `SubscriptionDiscountApplicability` before moving anybody: a
code authored for the monthly price must not follow a subscriber onto the annual one. Refused rather
than dropped — removing a promotion changes what they pay every period from here on, and an operation
they asked for a *price* quote on is the wrong place to do that silently. A discount whose duration is
spent or whose expiry has passed reduces nothing, so it never blocks a move.

`FamilyCode` and `FamilyRank` group ordinary plans into ordered product levels. A level remains a
plan—there is no second tier entity. Prices may carry `DisplayPriceNote` for authored presentation
such as "$17/month, billed annually".

Discounts are authored at `/api/subscription-discounts`, optionally scoped to an organization and
optionally restricted to plan codes, price identifiers, or both. Both lists are resolved against the
catalogue as the discount is authored — an identifier that does not exist in scope, or a price on a
plan the code does not cover, is refused with `subscription_discount_applicability_invalid`, because
the alternative is a discount that stores cleanly and then refuses every redemption. The two restrictions **narrow**
rather than offering two ways to qualify: with both set, both have to match, so a code aimed at the
yearly price is refused on the monthly one. A discount stored before price applicability existed
carries no price list and stays unrestricted by price. Unknown, retired, expired, inapplicable, and
wrong-currency fixed discounts are rejected. Accepted terms are copied onto the subscription, so retiring the
catalogue entry never changes an existing subscriber's renewal.

Campaign discounts use the same pricing pipeline but add a validity window and a durable redemption
ledger. `FirstAnnualPeriod` discounts both an opening calendar stub and the first full annual term;
the stub does not consume that annual benefit. `FreeOpeningCalendarPeriod` is always 100%, one use
per organization, and requires a saved payment method before activation even though its opening
invoice is zero. Its end date is optional: without one it remains available until archived.

Buyers validate a code through `POST /api/subscription-discounts/preview`. It uses the same request
and pricing path as subscription preview, performs no writes, and returns an undiscounted quote plus
a stable rejection status when the code is unknown, early, expired, inapplicable, already redeemed,
or temporarily unavailable. Management create/list/edit/archive and buyer preview currently
require an authenticated caller.

## Financial documents

This module issues its own invoices, trial invoices and credit notes. Stripe is still the payment
processor; it is no longer where the paperwork lives.

The provider's invoice used to be the only durable statement of what a subscriber was charged. That
put the record of our revenue inside somebody else's product: unreachable when they were down, gone
the day we changed processor, and shaped by their template rather than ours. It also meant the
signup payment had no invoice at all, because a hosted checkout composes none — so invoice history
began at the first renewal and a customer's very first charge was the one they could not get a
document for.

`SubscriptionFinancialDocument` is the aggregate, and it is append-only. Three types share two
number series:

| Type | Issued for | Series |
| --- | --- | --- |
| `Invoice` | Every settled positive charge: the initial checkout payment, a card-free trial's conversion charge, a renewal, a paid plan change, a paid quantity increase, metered overage. | `INV-{year}-{000001}` |
| `TrialInvoice` | Every trial start, card or no card. Zero total — it states the terms of a period nobody was charged for. | The same `INV-` series, so a subscriber can see they have every invoice. |
| `CreditNote` | A confirmed refund. Historically also a downgrade whose unused time was banked as subscription credit — no plan or quantity change banks credit any more, so no new one is raised for that reason. Linked to the invoice it adjusts where there is one. | `CRN-{year}-{000001}` |

Nothing is issued for a failed, abandoned or pending payment attempt, an ordinary cancellation, or
credit being *consumed* by a later invoice — that is a deduction on the invoice it paid for, and a
second document for it would count the same value twice.

### Exactly once, and why that is a unique index rather than a lock

Every document is keyed on the event that caused it — `FinancialDocumentSourceKey`: a payment detail
id, a refund id, a subscription plus a trial instant, or a change reference — under a unique index.
A redelivered webhook, a retried work item, two workers racing and the recovery sweep all derive the
same key, so the second attempt finds the first document instead of allocating another number and
sending another email.

Every derivation is from a durable identifier the source event already carries, never from a clock
or a counter. That is what lets a key be recomputed after the process that first computed it is
gone, which is the only situation in which recovery is needed at all.

Numbers come from an atomic `$inc` on one counter document per tenant, prefix and year. Allocation
happens *before* the insert, so a number taken by an attempt that then loses the duplicate race is
abandoned rather than reused. **The sequence therefore has gaps**, and that is the correct trade: a
gap is a question an auditor can answer from the ledger, while a reused number is two documents
claiming to be the same one, which nothing can answer.

### Nothing on a document is a reference

Every party, plan, price, period and amount is copied at issue. A document answers "what was true
when this was issued"; the catalogue answers "what is true now". A join between them would quietly
replace the first question with the second — so editing a billing profile, renaming an
organization, repricing a plan or changing the merchant's address affects documents issued from that
point on and nothing already sent.

Corrections are made by issuing a credit note and, where needed, a replacement invoice. No issued
financial field is ever updated. The only fields that move are the refund status — a summary of the
credit notes, kept so a list can be read without joining — and the delivery state.

### The PDF is written before it is recorded, and addressed by its own hash

Rendered from this module's own HTML template through the platform's PDF engine, stored through the
platform's storage driver, and hashed with SHA-256. An issued PDF is **never** regenerated against a
newer template: the file the subscriber already has is the document, and `TryRecordPdfAsync` refuses
the second write to enforce that rather than relying on anybody remembering it.

The storage key is the document id **plus the hash of the bytes**, and the bytes are written before
the key is recorded. Both halves matter. A headless-browser PDF carries generation metadata, so two
renders of one immutable document are not guaranteed to be byte-identical — under a shared key the
loser of that race could replace the winner's file *after* the winner's hash had been recorded,
leaving a document whose recorded hash described bytes that were no longer there. Writing before
recording means the recorded key always has its file; the reverse order would leave a document
pointing at nothing. A crash in between leaves an unreferenced object, which costs storage and
nothing else.

Delivery is scheduled as its own work, separately from issuing, so a template that throws or a
storage bucket that is unreachable retries all day without re-entering the code that allocates
numbers — and without the invoice looking unissued in the meantime. A document with no billing
email is rendered, left downloadable, and marked abandoned with `document_no_recipient`, because
retrying cannot conjure an address.

The mail is claimed before it is published, and **the claim is the authorisation to send**. Publishing
to the bus and recording that it was published are two writes with nothing joining them, so a crash
between them leaves a message that may or may not have gone out. `TryRecordMailRequestedAsync` is a
compare-and-set, so exactly one attempt ever wins the claim; an attempt that finds it already taken
with no recorded send does **not** publish.

**A publish that threw is the same situation, not the opposite one.** This is the part that is easy to
get wrong, and was: a throw is not evidence of non-delivery. A broker can accept a message and
acknowledge it and have the acknowledgement lost coming back, leaving the client holding a timeout for
a message that went out. So the claim is *never* released automatically, and a failed publish records
`document_mail_outcome_unknown` exactly as losing the claim race does.

That is **at most once, on purpose**, and it is the strongest guarantee available here:
`ConsumerMessage<T>` carries no identity a broker could deduplicate on, so exactly-once is not
reachable through this contract, and the choice is which way to fail. A subscriber may not receive an
email for an invoice they can still see in their history and download; the alternative is two identical
invoice emails, which reads as being billed twice and cannot be taken back.

What a *known* failure keeps is its retry: a render that failed produced nothing and is tried again.
Only an attempt that may already have handed a message to the bus gives up.

Which leaves the resend a deliberate act. `POST /api/subscriptions/invoices/{documentId}/resend`,
console only, reopens the delivery — giving the claim back, which nothing else does — and queues it on
the same work type every other delivery uses, so a resend cannot behave differently from a first
attempt. Whoever calls it is accepting that the subscriber may receive the invoice twice; that is a
judgement, and the point is that it is made by a person rather than by a retry policy.

Each resend that actually sends is **its own occurrence**. The queue admits one item per `(tenant,
work type, aggregate, key)` under a unique index that covers finished items as well as pending ones, so
the first delivery's key stays taken until that item passes its retention — and a resend scheduled
under it is refused as a duplicate of work that already ran. `Delivery.ResendCount` is incremented in
the same write that reopens the delivery, and the key becomes
`document:{documentId}:resend:{generation}`. The first delivery keeps the bare `document:{documentId}`,
so items queued before this existed are still addressed by the key they were queued under.
`DeliveryWorkKeyFor` composes both, in one place, because the issuer schedules the first delivery and
the resend schedules the rest and two spellings of one key is how a resend comes to be dropped in
silence.

**Concurrent resends collapse onto one generation.** Reopening is conditional on the document's send
being *finished with* — unclaimed, unsent, and not still pending — so the first request flips it out of
that state and every request arriving before the send happens joins the generation already going out.
Without that, two requests would mint two generations and two queue items which share the one
document-level mail claim: the first would send, the second would find the claim taken and send
nothing, and an operator would have two successes and one email.

Giving each generation its own claim would be worse, not better. It would let a double click put two
copies of an invoice in somebody's inbox, which is precisely what the claim exists to stop — so the
collapse is not a convenience, it is the only option that does not regress the guarantee.

The response reports what the request did rather than a success flag: `Queued` reopened and scheduled,
`JoinedPending` joined an outstanding send and scheduled nothing, `AwaitingSweep` reopened but the queue
write failed, so the delivery sweep will carry it — that sweep finds outstanding documents by their
delivery state and needs no key at all. All three are successes; the mail is going to be sent in all
three, which is why saying only that would be useless.

`Delivery.MailMessageId` is derived from the document id and travels in the mail's data context as
`MessageId`. Belt and braces rather than the mechanism: sending is already at most once, and the id
costs nothing, names the same thing in a log line and in a support conversation, and lets a mail
consumer that deduplicates catch anything a future change here lets through.

### The obligation, and why it is not the queue entry

A document is *owed* the moment the event happens. Recording that obligation and scheduling the work
are two different things, and only the first has to be durable.

`SubscriptionDocumentSource` is the obligation: appended to `SubscriptionDetail.PendingDocumentSources`
in the same write as the transition that caused it, exactly as `SubscriptionOutboxEvent` is appended
beside the state change it belongs to. It carries the plan, price, quantities and period **as they
were then**. It is pulled off once its document exists, so a healthy subscription carries none — and
any that remain are precisely what recovery is looking for.

Two problems, one record:

- **Durability.** Scheduling is a write to another database with no transaction shared with the
  money, so a crash in that window used to leave nothing behind but a payment. **No plan or quantity
  change banks new credit any more** — see [Plan changes and proration](#plan-changes-and-proration)
  — so nothing appends this source today. Historically, a change that *banked* credit rather than
  charging for it left nothing at all if the write were lost: the value was folded into
  `CreditBalanceMinor` and the balance could not say which change put it there. That source was
  appended inside the very compare-and-set that banked the credit — `TryChangePlanAsync` and
  `TryApplyQuantityChangeAsync` still accept the optional parameter, kept for the legacy consumer
  below rather than because either service still populates it — because anywhere else would have
  been a window in which the credit note was lost for good.
- **Historical accuracy.** A document written minutes or days late has to describe the terms the
  money was charged on. Reading them off the live subscription is correct only while nothing has
  changed since, which is the assumption a delayed or recovered issue breaks: an invoice for last
  month's renewal, written after a plan change, would name this month's plan and its unit price. The
  issuer prefers the frozen terms and falls back to the subscription only for events that predate
  this mechanism — logging when it does, because that is the one case where a document can name the
  wrong plan.

The amounts are deliberately *not* frozen for a charge. What was taken is on the payment, which is
the only version of the figures the bank agrees with; a second copy would be free to disagree with
it. The obligation freezes what the money was *for*. A legacy banked credit note — one of the
sources written before the credit-never-banks policy, still being drained; see
[Financial documents](#financial-documents) above — is the one exception and carries its own
figures, decomposed at the change from the outgoing side of the settlement — see
`FinancialDocumentCreditComposition` — because there is no payment to read them from and the
outgoing price's tax rate and mode are gone the moment the new plan replaces them.

Money paths call `ISubscriptionFinancialDocumentAnnouncer`, which records then schedules, in that
order. That is the only thing they know about documents, and neither step throws: by then the money
has moved, so a failed write costs a later document rather than a failed charge.

Two work types carry it. `FinancialDocumentIssue` composes and numbers — naming a payment, naming a
subscription (drain whatever it owes), or naming nothing, which is the recovery pass.
`FinancialDocumentDelivery` renders and posts. Both are the lowest priority in the queue: nothing
about a document affects entitlement or money, and it must never delay a renewal that does.

### Recovery has no window

Four passes, and not one of them looks back a fixed number of hours. A lookback makes recovery a
function of how long the worker was away: an outage longer than the window leaves documents that are
never issued, and nothing that says so. That is monitoring dressed as recovery.

| Pass | Finds | How it is bounded |
| --- | --- | --- |
| Recorded obligations | Anything a transition recorded and nobody wrote | Not bounded in time at all. A partial index on `PendingDocumentSources.0` existing holds only the subscriptions currently owing something, so "which ones, ever?" costs what "which ones this hour?" would. |
| Settled charges | A charge whose obligation record was itself lost | A stored high-water mark, walked forward |
| Confirmed refunds | A refund's credit note | A stored high-water mark, walked forward |
| Trials | A trial predating this mechanism, or whose record was lost | A stored high-water mark over the trial start |

The marks live in `SubscriptionDocumentCursors`. A mark is a **position, not a moment**: the instant
of the last record accounted for *and which record that was*, because several records routinely share
an instant and an instant alone cannot name a place in a sequence. Each pass reads a keyset page —
everything strictly after that position — and moves the mark to the last record it accounted for.

A full page is **not** a reason to hold the mark back. That was the first attempt at this and it was a
livelock: a tenant with more than one page of history re-read the same page forever and never reached
anything after it, with every pass looking healthy and issuing nothing. The mistake was conflating two
different situations — records tied on one instant, where advancing past the instant would skip a
twin, and a page simply being full, where the last record read is a perfectly safe place to resume.
Carrying the identifier resolves both: the order is total, so resuming after the last record reaches
the next one whether or not the page was full, and nothing is re-read.

Writes are monotonic over the whole mark, so two workers sweeping one tenant converge on the furthest
either reached rather than taking turns dragging it backwards. Deliberately not `$max`, which was
enough while a mark was one instant and is not now: comparing on the instant alone would refuse a mark
that advanced *within* an instant, which is exactly how a page of tied records makes progress.

The write is a conditional update followed by an insert-only upsert — the two cannot be combined,
because an upsert filtered on "the stored mark is older" matches nothing when it is newer and then
tries to insert a second document under the same `_id`. And it **loops**, because those two steps are
not one atomic act: workers starting a tenant from scratch all find nothing to update and all go on to
insert, and every one but the winner inserts nothing. A writer that walked away there would leave the
winner's mark standing even when its own was further along, and the sweep would re-read records it had
already accounted for. Round again, and a document now exists, so the conditional update either moves
the mark or correctly declines.

A pass that read nothing does not move its mark at all. Advancing to "now" would be wrong — finding
nothing proves only that nothing is there yet, and a record can still arrive stamped earlier than the
pass that looked.

Refunds reach this module *only* by polling. A refund confirms inside the payment module, which must
never depend on subscriptions, so nothing there can announce it and this side has to come and look.
A deliberate cost of keeping the dependency one-directional.

### Reading them back

`GET /api/subscriptions/invoices` answers from the ledger, filterable by subscription, type, status
and issue-date range. `GET /api/subscriptions/invoices/{documentId}/pdf` serves the bytes — never a
storage or provider URL, either of which is a bearer token for the document that cannot be revoked
by revoking the caller's access. A payment id is also accepted there, so links from the previous
payment-derived history keep working; only a payment from before the ledger existed falls through to
the provider's stored copy, and that fallback is deprecated.

## Billing profile

`SubscriptionBillingProfile` is who an organization's documents are addressed to: a legal name, a
billing contact, and optionally an address and a tax registration number. One per organization.

Distinct from `BillingAccount`, which is an organization's standing with one payment *provider* — a
customer id, a saved card, the merchant scope that took the money. An organization can hold several
of those and they must all print the same name.

A complete profile is required before a paid subscription starts and before any money-moving change,
enforced by `ISubscriptionBillingProfileGuard` and refused with
`subscription_billing_profile_incomplete` naming the missing fields. Free plans, previews, quantity
decreases and renewals are never blocked: none of them is a moment a person can be asked to fill in
a form, and a renewal that refused to charge over a form would cost the subscriber their service.
`RequireBillingProfile` turns the requirement off for an installation mid-migration.

The address and the tax id are deliberately not required. A great many subscribers are individuals
with neither, and refusing them a subscription over a field their jurisdiction does not ask for would
be a billing rule invented here.

The profile is also **where a billing account gets its contact**, on every subscribe, when
`CreateSubscriptionRequest` names none. `BillingName` and `BillingEmail` stay on the request for an integration that keeps its own
record of a customer, and each falls back on its own field: a caller that sends an address and no name
keeps the profile's name, because it meant the address and blanking the name would lose the only one
there is. That decides where renewal and usage-threshold mail is sent and nothing else — what a
document states about its recipient is snapshotted at issue, never read from here.

Read through the guard rather than by injecting the repository a second time, for the reason the guard
exists: the profile is one organization's answer to "who do we bill", and two services reading it two
ways is how they come to disagree. Unlike the completeness check it is *not* gated on
`RequireBillingProfile` — a free metered plan is never asked for a profile and still sends
usage-threshold mail, so the address is worth having wherever there is one.

`GetOrCreateAndReconcileAsync` applies it to an **existing** account too, and not only to a new one.
A billing account is one per organization and provider and outlives every subscription on it, so an
organization that subscribed before filling its profile in used to keep the blank contact for good:
correcting the profile and subscribing again returned the old account untouched, and renewal and
threshold mail went on going nowhere. Creating it correctly was never enough.

The reconciliation is a single upsert keyed on the unique index, so there is no read-then-write window
and concurrent signups converge on one document. A null leaves what is stored alone rather than
blanking it — a caller naming only an address cannot erase a name — but a value that *is* supplied
overwrites, which is what makes a corrected profile take effect. The consequence worth knowing: an
integration that sets a contact once and later subscribes without naming it will see the profile's
value take over, so send it on every request if you keep your own record of the customer.

Contacts are recorded per user id as people act, and under **that person's own name and address**,
taken from the authenticated context. Not the organization's billing contact: those two are the same
person only by coincidence, and copying the second — which this used to do — made every document say
the finance mailbox had changed the plan, whichever employee actually did. Recorded when they act
rather than looked up when the document is written, because an identity directory answers only about
now and people leave and rename.

A worker renewal names `System renewal` and no user, because none acted: naming whoever last touched
the subscription would attribute a charge to somebody who may have left a year ago. `System renewal`
is reserved for that — a refund credit note says `System refund`, because a refund is not a renewal
and a document should not say it was.

## Merchant profile

`SubscriptionMerchantProfile` is who is *selling*: one per **tenant**, holding the legal name and
optionally a trading name, address, tax registration, support address and payment instructions. The
counterpart of the billing profile, and snapshotted onto every document in the same way.

Stored per tenant rather than read from configuration because this platform runs many tenants against
one deployment, and an invoice names a seller in law. A single configured identity had every tenant
issuing documents under one company's legal name, address and tax registration — not a presentation
defect but a false statement on a financial record.

Writable by the platform console alone, using the same boundary that decides who may name an
organization (`PaymentOrganizationScope`). A subscriber able to set this could have their own invoices
issued under a company of their choosing. Readable by any authenticated caller in the tenant, because
it is printed on every document they have already been sent.

`Subscription:Invoicing:*` remains as the fallback for a tenant that has not filled one in, so
upgrading does not blank the seller on every document issued between the deployment and somebody
noticing. The response flags that with `isInheritedFromConfiguration`, which is the one thing a
console has to make visible: those values are shared with every other tenant. While
`RequireBillingProfile` is on, a tenant with no seller named anywhere reports `merchantLegalName`
alongside the subscriber's own missing fields — both halves are required for the same reason.

`ISubscriptionMerchantProfileService.ResolveAsync` never fails and never blocks issuance. By the time
a document is being composed the money has moved, and refusing to record it because nobody filled in
a form would lose the record of a real payment. Enforcement belongs before the charge, where refusing
costs nothing.

`PUT /api/subscription-plans/prices/{priceId}/archive` retires a price without changing any
subscription that already holds its snapshot.

Under the `Subscription` section. The ones whose default is a decision:

| Setting | Default | Why it matters |
| --- | --- | --- |
| `TenantIds` | *(empty)* | Pins the sweep to specific tenants. Empty discovers them from the registry. |
| `TenantRefreshSeconds` | `300` | How long a discovered roster is reused. Nothing time-critical waits on it. |
| `EntitlementCacheSeconds` | `10` | How stale an entitlement answer may be. Counters are never cached. |
| `CounterRetentionDays` | `400` | How long a finished period's counter is kept. Long enough for a billing dispute. |
| `InitialChargeGraceMinutes` | `60` | How long an unpaid subscription waits before it is treated as abandoned. |
| `ReconciliationPollSeconds` | `120` | Clamped to a 30 second minimum. The repair sweep only; the queue poll is separate and shorter. |
| `RenewalBatchSize` | `50` | How many due subscriptions one sweep pass takes. |
| `CancellationBatchSize` | `50` | How many scheduled cancellations past their period end one sweep pass carries to effective. |
| `DunningMaxAttempts` | `4` | Attempts, including the first decline, before a subscription moves to `Unpaid`. |
| `DunningRetryIntervalHours` | `24` | Fixed interval between dunning attempts. |
| `UsageRatingBatchSize` | `50` | How many subscriptions one usage-closing sweep pass takes. |
| `UsageRatingMaxAttempts` | `3` | Overage-charge attempts before an invoice is abandoned. Independent of `DunningMaxAttempts` — a failed overage charge never affects the subscription. |
| `UsageRatingRetryHours` | `24` | Fixed interval between overage-charge retries. |
| `MaximumUsageMetadataEntries` | `10` | Bounds what a product can attach to a billing record. |
| `RequireBillingProfile` | `true` | Whether a paid subscription or money-moving change needs a complete invoicing identity. Off is for an installation mid-migration. |
| `DocumentDeliveryMaxAttempts` | `8` | Render-and-email attempts before a document is abandoned. Independent of every other retry budget — a failed render never affects money. |
| `DocumentDeliveryBatchSize` | `25` | How many outstanding documents one sweep pass takes. |
| `DocumentFirstPassReachDays` | `400` | How much pre-existing history a tenant picks up the *first* time the document sweep runs against it. Used once per tenant; every pass after it starts from a stored high-water mark that only moves forward, so this bounds nothing ongoing. |
| `Invoicing:*` | *(empty)* | The **fallback** merchant identity, for a tenant with no stored merchant profile: legal name, address, tax id, support email, payment instructions. Shared by every tenant on the deployment, which is why a stored profile supersedes it. |

A currency must also exist in `Payment:CurrencyMinorUnits` or a price in it can never be
charged. That is validated when the price is authored rather than at checkout, where the same
mistake reaches a customer who has already chosen a plan.

## Before the first tenant goes live

## Financial observability and audit

Every subscription command and every renewal charge writes the same structured lifecycle
vocabulary: `OperationId`, `CorrelationId`, operation, stage, outcome, source, attempt, amount and
currency. Request correlation IDs connect API logs to the response's `X-Correlation-ID`; renewal
operation IDs are stable across worker retries, so a retry is one timeline rather than an
unrelated incident.

Operational logs hash tenant, organization and subscription identifiers. The append-only
`SubscriptionAuditEvents` collection keeps the real tenant-scoped identifiers needed to
investigate a financial dispute. It has no TTL and exposes no update/delete operation. Neither
store may contain card data, stored-payment-method IDs, provider customer IDs, checkout URLs,
access tokens, webhook bodies or secrets.

`GET /api/subscriptions/{subscriptionId}/audit?limit=100` returns the caller's organization-scoped,
sanitized timeline. Actor and payment identifiers remain restricted to the database. Audit writes
are deliberately fail-open after a business operation: an unavailable audit store emits the
critical marker `SUBSCRIPTION_AUDIT_WRITE_FAILED`, but never makes a caller retry money movement.
Alert on that marker; the payment and subscription ledgers remain the reconciliation source.

Audit records answer who/what/when and the resulting state. Structured logs answer how the code
got there, including exceptions and timing. Both are required: ordinary logs are mutable and
retention-bound, while an audit trail alone is intentionally too small to debug execution.

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
