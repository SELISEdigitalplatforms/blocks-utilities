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

**Plan and quantity changes are refused while a year is pending**, with
`subscription_initial_annual_period_pending`. Repricing then would have to unpick a settled annual
charge or silently discard one about to be collected, and neither is something a caller can be told
about after the fact. The wait is at most a month.

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

A positive difference is charged immediately; a negative one is banked as credit. The target
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

A discount reduces a renewal only while `DiscountTerms.DurationPeriods` and `ExpiresAtUtc` still
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

## Invoice boundary

The signup payment remains a hosted checkout/PaymentIntent and has no Stripe invoice. Invoice
history therefore begins at the first settled renewal (and also includes later plan-change and
usage invoices). `GET /api/subscriptions/invoices` returns authenticated PDF download links; the
provider's permanent document URL is never exposed.

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
| `ReconciliationPollSeconds` | `120` | Clamped to a 30 second minimum. |
| `RenewalBatchSize` | `50` | How many due subscriptions one sweep pass takes. |
| `DunningMaxAttempts` | `4` | Attempts, including the first decline, before a subscription moves to `Unpaid`. |
| `DunningRetryIntervalHours` | `24` | Fixed interval between dunning attempts. |
| `UsageRatingBatchSize` | `50` | How many subscriptions one usage-closing sweep pass takes. |
| `UsageRatingMaxAttempts` | `3` | Overage-charge attempts before an invoice is abandoned. Independent of `DunningMaxAttempts` — a failed overage charge never affects the subscription. |
| `UsageRatingRetryHours` | `24` | Fixed interval between overage-charge retries. |
| `MaximumUsageMetadataEntries` | `10` | Bounds what a product can attach to a billing record. |

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
