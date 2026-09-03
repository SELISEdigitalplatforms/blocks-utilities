# The subscription lifecycle

The whole journey — signup to cancellation — with every alternate path, what the subscriber sees,
and a test case for each stage.

Companion to [the plan authoring guide](../plan-authoring/), which covers the plan builder's
options. This one covers what happens to a subscriber once a plan exists.

---

## The map

```
                            ┌──────────────┐
              signup ──────▶│  Incomplete  │──── never confirmed ───▶ IncompleteExpired
                            └──────┬───────┘
                    webhook confirms │
                    ┌────────────────┴────────────────┐
                    ▼                                 ▼
             ┌─────────────┐                    ┌───────────┐
             │  Trialing   │───trial ends──────▶│  Active   │
             └─────────────┘   (charged)        └─────┬─────┘
                    │                                 │
                    │ no card at trial end            │ renewal declines
                    │                                 ▼
                    │                          ┌────────────┐
                    │                          │  PastDue   │ grants, in grace
                    │                          └─────┬──────┘
                    │                                │ attempts exhausted
                    └──────────────┬─────────────────┘
                                   ▼
                            ┌────────────┐
                            │   Unpaid   │ grants nothing
                            └────────────┘

           any of the above ──── cancel ────▶ Canceled
```

| Status | Grants entitlements? | Meaning |
| --- | --- | --- |
| `Incomplete` | **no** | Created; first charge or card setup unconfirmed |
| `IncompleteExpired` | no | The first charge never completed |
| `Trialing` | **yes**, subject to trial grants | In trial |
| `Active` | **yes** | Paid and current |
| `PastDue` | **yes, during the grace period** | Money owed, retries running |
| `Unpaid` | **no** | Dunning exhausted, or no card to charge |
| `Canceled` | no | Ended |

`PastDue` still granting is deliberate: you do not cut off a paying customer over a card that
expired.

---

## Stage 1 — Signup

### The fork

| Opening amount | Card required (§19 of the guide) | What the subscriber gets |
| --- | --- | --- |
| more than zero | — | A payment checkout |
| zero | no | Immediate activation, no checkout at all |
| zero | yes | A **card-setup** checkout that charges nothing |

The opening amount can be zero for several unrelated reasons: a trial, a 100% promotion, a calendar
stub that rounds to nothing, or a genuinely free tier.

### Activation waits for the webhook

**A subscription becomes active only when the payment carries both a confirming status and a
webhook confirmation.** The shopper's return from checkout is not evidence — a redirect can be
replayed, forged, bookmarked, or lost when someone shuts the laptop.

**Clients should expect a brief `Incomplete` window after paying.** Poll or refresh; do not treat the
redirect as success and do not show an error.

The same holds for card setup, on the setup-confirmed event. The setup record settles as authorized
and never captures, which keeps every total that sums captured money from picking it up.

### What is frozen at signup

The plan and price are **copied** onto the subscription, not referenced. So:

- entitlement is one document read with no join
- **editing the catalogue is never retroactive** — a subscriber keeps the terms they were sold until
  something deliberately migrates them

Also frozen: the opening charge amount, whether it was prorated, the proration day counts, the trial
terms, the usage cadence, the tax and discount metadata.

> ### Test 1.1 — the incomplete window is real
> Complete a checkout and read the subscription immediately.
> → Likely `Incomplete`. Read again a moment later → `Active`. A UI that errors on the first read is
> wrong.

> ### Test 1.2 — catalogue edits do not reach existing subscribers
> Subscribe. Edit the plan's price. Read the subscription.
> → Unchanged. (And the plan itself becomes uneditable once anything has subscribed, in **any**
> status — cancelled included.)

---

## Stage 2 — The opening period

### Anniversary alignment

The period runs signup-day to signup-day. No stub, no proration, first charge is the full amount.

### Calendar alignment, monthly

Signup 25 August → the period `[25 Aug, 1 Sep)`, charged **7/31** of the monthly amount, then the
1st of every month thereafter.

Proration uses **actual calendar dates**, inclusive, over the actual length of that month. February
uses 28 or 29. The **time of day never enters into it**, and the **subscriber's own time zone**
decides which date it is — a signup on the local 1st is a full period and is not reported as
prorated.

### Calendar alignment, yearly

The year anchors on the 1st of the month **after**, and the stub is priced from the linked monthly
price (see guide §14). Two collection timings:

| | At checkout | On the 1st | Cancel during the stub |
| --- | --- | --- | --- |
| **At boundary** | the stub | the year is charged | year never charged |
| **Up front** | stub **and** year | nothing charged | nothing refunded, access runs the full year |

Between signup and the boundary the subscription carries a **pending annual period** whose figures
are frozen at checkout creation and never recalculated — that boundary is a month away, and a charge
that re-derived its amount could take a different sum than the subscriber agreed to.

The boundary charge and the period it opens are written in **one transition**, so opening the year
and forgetting it was pending cannot come apart. A decline there leaves the year pending and enters
ordinary dunning, so the retry owes exactly the frozen amount.

**Change is restricted while a year is pending:**

| State | Plan / quantity change |
| --- | --- |
| Year **unpaid** | **Refused.** A downgrade or decrease may still be *scheduled* for the boundary. |
| Year **prepaid**, change keeps cadence and boundary (upgrade, or any quantity increase) | Allowed — the stub's remaining days and the paid year settle together in one immediate charge |
| Year **prepaid**, change would re-cadence it | Waits for the boundary |

> ### Test 2.1 — stub arithmetic
> Calendar monthly, CHF 950, signup 25 August → **CHF 214.52**. Signup 25 February (non-leap) →
> **CHF 135.71**. A 30-day assumption gives 126.67 and is wrong.

---

## Stage 3 — Trial (if any)

Covered in guide §17. The lifecycle-relevant parts:

- A trial **grants entitlements**, subject to trial grants (which can be smaller than the plan's
  normal allowance).
- The trial end is an **exclusive** boundary — show it as "through August 31", not "ends
  September 1".
- The trial end **is** the subscription's next billing moment, so the ordinary renewal sweep picks
  it up exactly as it picks up a renewal.
- **A trial changes plan with no charge and no credit at all.** Nothing has been paid for, so there
  is nothing to prorate — the plan, price and quantity snapshot simply swap. A trial's plan change is
  therefore always immediate, even if it is a downgrade.

### Trial end, both ways

| | Result |
| --- | --- |
| Card on file | Charged for the first paid period → `Active` |
| No card | → **`Unpaid` immediately. No `PastDue`, no retries.** |

Retrying a charge with nothing to charge cannot succeed on attempt two any more than attempt one.

### Conversion timing

A payment-free trial ending mid-month charges a stub from the trial-end date to the next 1st, keyed
to the period the **trial ended in** — never the sweep's clock. A conversion discovered late still
bills the stub it owes, then raises the following period separately. A trial ending on the 1st gets
a full period.

> ### Test 3.1 — card-free trial end
> No-card trial. Let it lapse. → `Unpaid` immediately; audit trail shows **no retry attempts**;
> entitlements stop. Add a payment method → the outstanding amount is charged **straight away**
> (unlike adding one mid-trial, which charges nothing).

---

## Stage 4 — Steady state

Two independent clocks run from here.

```
Fee clock    ─── renews, charges, can decline into dunning
Usage clock  ─── resets allowances, rates overage, charges separately
```

They are genuinely independent: **a declined overage charge never changes the subscription's status,
and a declined renewal never touches the allowance.**

### The fee clock

Periods are **derived, never advanced**. Asking which period an instant falls in recomputes it, so
nothing has to notice a boundary passing — and there is no rollover job that can run twice or not at
all. Month ends **clamp on read, never on write**: an anchor on the 31st bills on the 28th in
February and returns to the 31st in March.

### The usage clock

Each window is addressed by its own key, so crossing a boundary simply addresses a different
document, which the next write upserts at zero.

At the close of each usage period, the balance is priced against the plan snapshot's meter:

- **only the excess** (`usage − includedQuantity`) is priced
- bands are **graduated**, counted from the first overage unit inclusive
- the result is recorded as a usage invoice **before** the charge is attempted, so a crash mid-attempt
  is recoverable
- a sweep that missed several months closes **every** intervening period

### Reading state

- **Entitlements** — advisory. Two callers at 499 of 500 both get "one left". Reads only our own
  database, so **if the provider is down every existing customer keeps working.** Subscription
  cached briefly; **counters never cached**.
- **Usage recording** — authoritative. Returns the balance including the caller's own contribution.
  `enforce` refuses and rolls back past the allowance, but **only on a meter with no overage**.

> ### Test 4.1 — the clocks are independent
> Yearly plan, monthly allowance. Advance one month.
> → No invoice; allowance reset; new period key. Now force an overage charge to decline.
> → Usage invoice abandoned after its retries; subscription still `Active`.

---

## Stage 5 — Changing quantity

Asymmetric by design.

| Direction | Timing | Money |
| --- | --- | --- |
| **Increase** | `Immediate` | Prorated charge for the rest of the period, taken now |
| **Decrease** | `NextPeriod` | **Zero. Never refunded.** They keep the units until the period ends. |

**Real life:** adding a phone line mid-month bills a part-month immediately; cancelling one takes
effect at the end of the cycle with no money back for the days remaining.

A scheduled decrease is held as a pending change and can be withdrawn.

Two guarantees in the flow:

- **Preview before apply, always.** Editing any quantity **discards** the quote — a confirmation
  sent after its figures stopped applying is a confirmation of numbers the subscriber never saw.
- **The client names the quantity; the server names the band and the price.** Letting a client name
  the band would let it name a price the plan may not agree to.

An increase also re-evaluates the volume band, so the unit price can fall at the same time.

> ### Test 5.1 — both directions
> Held 4, band `5–10 → 10%`. Increase to 7 → applied now, prorated charge > 0, next renewal reflects
> the band. Decrease to 4 → scheduled, **charge 0**, still holding 7 today. Cancel the pending
> change → back to 7 permanently.

---

## Stage 6 — Changing plan

`PlanChangeClassifier` reads the settlement **before any credit pays for it**:

| Settlement | Timing | Money |
| --- | --- | --- |
| Worth **more** than what it replaces | **Immediate** | Prorated difference charged now |
| Worth the **same or less** | **Waits for the boundary** | Nothing charged, nothing refunded |

The unused value of the outgoing period and the cost of the same remaining time on the target price
both run through the **exact** gross-and-discount maths a renewal uses — the subscriber's discount
applies to both sides identically, because it belongs to them, not to whichever plan they are on.

### Rules sitting above that arithmetic

- **A trial is always immediate** — it has paid for nothing, so there is no paid period to protect.
- **A paid annual term being re-cadenced always waits.** Annual → monthly tends to settle *positive*,
  because a month costs more than the remaining slice of a discounted year; charging it now would
  bill the same weeks twice.
- **Nothing creates credit. There is no refund path.** Credit already held is spent against an
  immediate upgrade and any remainder persists, but a settlement worth less than what it replaced
  hands nothing back. Nothing in the module ever produces a negative amount.
- **One pending commercial change at a time.** A booked plan change refuses a quantity change and
  vice versa.
- Moving onto a **calendar-aligned** price installs that price's boundaries there and then. Two
  prorations meet and are deliberately different kinds: what they are *leaving* is credited by
  elapsed time; what they are *buying* is priced by calendar dates — the same 7/31 a fresh signup
  that day would pay.

### Restrictions

| Refused | Why |
| --- | --- |
| Different currency or billing interval | Would mean rebuilding the period boundaries mid-flight |
| `PastDue` / `Unpaid` | A customer who owes money changing plans is a support decision, not an automated one |
| `Incomplete` | Has not paid yet — continue or cancel that checkout instead |

Downgrade below current usage should also be blocked by your own product rule: if the target tier's
`maxQuantity` is below what the subscriber currently holds, the change cannot succeed.

> ### Test 6.1 — the classifier
> Tier 2 → Tier 3 mid-period → **immediate**, prorated charge. Tier 3 → Tier 2 → **scheduled for the
> boundary**, charge 0, still on Tier 3 today. Do the same two from a trial → **both immediate, both
> free**.

---

## Stage 7 — Renewal and dunning

```
Active/Trialing  --success-->  Active (period advances, dunning cleared)
Active/Trialing  --decline-->  PastDue (attempt 1, retry scheduled)
PastDue          --decline, attempts remaining-->  PastDue (attempt N)
PastDue          --decline, attempts exhausted-->  Unpaid
any              --no stored payment method-->     Unpaid (immediately, no retries)
```

Defaults: **4 attempts** including the first decline, **24 hours** apart. A fixed interval rather
than exponential backoff — this is a business cadence for asking a customer to fix a card, not
load-shedding against a failing dependency.

Throughout `PastDue`, **entitlements still resolve**. That is the grace period. At `Unpaid` they
stop.

A normal renewal, a dunning retry, and a trial converting to paid are **one method**, because all
three are "charge the stored card for the period that is due" and none needs to know which it is.

Recovery from `Unpaid` is the add-payment-method flow; once the card is stored the outstanding
amount is charged immediately.

> ### Test 7.1 — the dunning ladder
> Force a renewal decline. → `PastDue`, entitlements **still granted**. Let all attempts fail →
> `Unpaid`, entitlements **stop**. Add a card → charged at once, back to `Active`.

---

## Stage 8 — Cancellation

Two modes:

| Mode | Effect |
| --- | --- |
| **At period end** (default) | Keeps granting until the paid period ends |
| **Immediately** | Stops right away |

**No refunds, ever** — consistent with the whole module. Cancelling on the same day as subscribing
returns nothing.

Cancellable from `Incomplete`, `Trialing`, `Active`, `PastDue` **and** `Unpaid`. That last one
matters: a subscriber offered nothing but "recover" has no way to walk away instead.

Cancelling while a card setup is outstanding settles it, so completing the form afterwards cannot
start a subscription somebody has cancelled.

> **Known gap, stated rather than built around:** a cancelled subscription's still-open **final usage
> period is never rated.** An immediate cancellation clears the usage billing date the moment
> entitlement stops, so usage recorded in that unrated final stretch has no billing path today. If
> your product allows heavy metered usage right up to cancellation, know that this window is free.

> ### Test 8.1 — both modes
> Cancel at period end → still `Active`-equivalent access until the boundary, then `Canceled`.
> Cancel immediately → entitlements stop now, no refund for the remaining days.

---

## What is left behind

| Artifact | Notes |
| --- | --- |
| **Invoice** | Every settled positive charge: initial payment, trial conversion, renewal, paid plan change, paid quantity increase, metered overage |
| **Credit note** | A confirmed refund. No plan or quantity change banks credit any more, so none is raised for that reason |
| **Usage ledger** | Append-only, **never expires**. A correction is a reversal entry, never an edit, so a bill can always be explained |
| **Usage counters** | Expire on a retention setting (default 400 days). Recomputable from the ledger |
| **Audit trail** | Immutable lifecycle trail; deliberately thin — no actor id, no payment id — so it can be shown to whoever is looking |

Overage is **one aggregated charge per period, not one per meter** — per-meter line items are
recorded on the invoice for support traceability, but the charge itself is the total.

---

## Quick reference: what charges when

| Event | Charged | When |
| --- | --- | --- |
| Signup, anniversary | Full amount | Now |
| Signup, calendar monthly | Prorated stub | Now |
| Signup, calendar yearly, at boundary | Stub | Now; year on the 1st |
| Signup, calendar yearly, up front | Stub + year | Now |
| Trial start, card required | Nothing | — |
| Trial end, card on file | First paid period | At trial end |
| Trial end, no card | Nothing — goes `Unpaid` | — |
| Quantity increase | Prorated difference | Immediately |
| Quantity decrease | Nothing | — |
| Plan upgrade | Prorated difference | Immediately |
| Plan downgrade | Nothing | — |
| Renewal | Full amount | At the boundary |
| Overage | Excess × graduated bands | At each **usage** period close, separately |
| Cancellation | Nothing, no refund | — |
