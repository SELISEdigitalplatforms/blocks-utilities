# Plan authoring guide

Every option in the plan builder: what it means, the alternatives, when to pick which, the real-life
thing it corresponds to, the core logic, and a test case that proves you chose right.

The running example is a four-tier professional plan — flat monthly fee, a user cap, and a pooled
monthly screening allowance with paid overage. It is a real shape and it exercises almost every
option in the builder.

| Tier | Users | Monthly | Included screenings/month |
| --- | --- | --- | --- |
| 1 | up to 3 | CHF 290 | 150 |
| 2 | up to 9 | CHF 950 | 450 |
| 3 | up to 24 | CHF 2,500 | 1,200 |
| 4 | up to 40 | CHF 5,000 | 2,000 |
| 5 | 41+ | custom | custom |

Yearly billing on every tier is twelve monthly payments less 8%.

---

## Contents

1. [Quantity vs meter — the fork everything hangs off](#1-quantity-vs-meter)
2. [Quantity items](#2-quantity-items)
3. [Volume discount bands](#3-volume-discount-bands)
4. [Meters](#4-meters)
5. [How usage is measured — aggregation](#5-how-usage-is-measured--aggregation)
6. [Allowance resets — reset policy](#6-allowance-resets--reset-policy)
7. [Carry-forward cap](#7-carry-forward-cap)
8. [The allowance clock — usage interval](#8-the-allowance-clock--usage-interval)
9. [Overage and rate tables](#9-overage-and-rate-tables)
10. [Notification thresholds](#10-notification-thresholds)
11. [What the plan grants — entitlements](#11-what-the-plan-grants--entitlements)
12. [Prices, and what they multiply](#12-prices-and-what-they-multiply)
13. [Billing alignment](#13-billing-alignment)
14. [Calendar-aligned yearly — stub base price and charge timing](#14-calendar-aligned-yearly)
15. [Automatic discounts and how three reductions combine](#15-automatic-discounts)
16. [Tax](#16-tax)
17. [Trials](#17-trials)
18. [Trial grants](#18-trial-grants)
19. [Payment method upfront](#19-payment-method-upfront)
20. [Plan families, archiving, and Tier 5](#20-plan-families-archiving-and-tier-5)
21. [The complete worked plan](#21-the-complete-worked-plan)
22. [Authoring mistakes that pass validation](#22-authoring-mistakes-that-pass-validation)

---

## 1. Quantity vs meter

**The single most common authoring confusion.** Get this wrong and you don't just get the wrong
price — you get the wrong machinery.

| | Quantity | Meter |
| --- | --- | --- |
| Question | How many did you *buy*? | How much did you *use*? |
| Who sets the number | The customer, deliberately | The system, from recorded events |
| When it changes | Only on upgrade/downgrade | Continuously |
| Bill known | **Before** the period | **After** the period |
| Machinery it feeds | Proration, credit, settlement | Allowances, resets, rating |

**Real life.** Quantity is the office coffee subscription: *"send 10 bags a month"* — you pay for 10
whether you drink them or not. Meter is the electricity bill: nobody buys 400 kWh in advance; the
meter spins and you are billed for what it read.

**Two tests that settle it.**

- *Can the customer tell you the number on signup day?* Yes → quantity. "It depends what happens"
  → meter.
- *If they take a month off and touch nothing, do they still pay?* Yes → quantity (you rent them
  capacity). Near zero → meter (you sell them consumption).

**Nouns you hold are quantity; verbs you perform are meters.** API keys are quantity. API calls are
a meter. Same feature area, opposite models.

**The middle cases people get wrong:**

| Sounds like | Actually is | Why |
| --- | --- | --- |
| "1,000 documents included" | **Meter** with `includedQuantity: 1000` | Nobody *buys* 1,000 documents; they use them. An allowance is meter territory. |
| "100 GB storage blocks — buy 1, 2, or 3" | **Quantity** | The customer picks a number and pays per block. |
| "pay for whatever GB you store" | **Meter**, `LastValue` + `Never` | Nobody picked a number. |
| "up to 40 users, flat fee" | **Quantity** that no price multiplies | The cap must be enforced; the fee must not move. See §2. |

> ### Test case 1.1 — you chose the right model
> Ask the plan's owner: *"If a customer signs up today and does nothing at all for a month, what is
> the invoice?"*
> - A confident exact figure → the thing is **quantity** (or a flat fee).
> - "Depends" or "near zero" → it is a **meter**.
>
> If the answer is a confident figure but you modelled it as a meter, subscribers will be billed
> nothing and you will not find out until the first month closes.

---

## 2. Quantity items

A quantity item is a number the customer holds. It has five fields.

```
itemKey:         user       ← internal name; prices point at this
unitLabel:       user       ← the word shown in the UI ("Per user")
minQuantity:     1          ← fewest they may hold
maxQuantity:     40         ← most they may hold (null = unbounded)
defaultQuantity: 1          ← what a new subscriber starts with
```

**These are three different numbers and "up to 40 users" is only the ceiling.** It is not what they
get and not what they pay for.

### The alternative most people miss: an unpriced quantity item

A quantity item that **no price multiplies** is valid — the schema only checks the reverse (a price
must name an item that exists). This turns it into a **pure capacity counter**:

- the change-quantity flow still enforces `min`/`max` — a 41st user is refused
- because every price is a flat fee, the charge never moves

That is exactly the tiered shape above: *"each tier is a fixed fee, not a per-user price; adding
users within the tier's capacity does not increase the fee."* One object satisfies both halves.

### Enforced vs advertised minimums

`minQuantity` **is** enforced. If your marketing says "Tier 2: 4–9 users" but you also want a
2-person firm to be allowed to buy Tier 2 for the bigger allowance, then `minQuantity` must be
**1**, and "4–9 users" belongs in the plan description or the price's display note. Author the
marketing range as the technical minimum and a two-person firm literally cannot buy the plan.

### When to use what

| Situation | Configure |
| --- | --- |
| Per-seat SaaS — price scales with seats | Quantity item **+ a price that multiplies it** |
| Flat tier with a hard capacity ceiling | Quantity item **+ flat-fee prices only** |
| Everyone gets the same fixed number, no choice | `min = max = default`, or skip the item and use an entitlement |
| Truly unlimited | No quantity item, or `maxQuantity: null` |

> ### Test case 2.1 — the cap is real, the price is not
> Plan: `user` item `min 1 / max 9 / default 1`, price CHF 950 **Flat fee**.
> 1. Subscribe. → holds 1 user, charged CHF 950.
> 2. Change quantity to 6. → **applies immediately; prorated charge is CHF 0**; next renewal still CHF 950.
> 3. Change quantity to 10. → **refused**, `maxQuantity` violated.
>
> If step 2 charges anything, your price is not a flat fee — check "What this multiplies".

> ### Test case 2.2 — the advertised minimum is not enforced
> With `minQuantity: 1` on a plan marketed as "4–9 users": subscribe with quantity 2. → **allowed**.
> If it is refused, someone authored the marketing range into `minQuantity`.

---

## 3. Volume discount bands

Optional bands on a quantity item: hold more, pay less per unit.

```
1–4    →  0% off
5–10   → 10% off
11+    → 15% off
```

**Volume, not graduated.** The band is chosen by the *whole* quantity and its discount applies to
the *whole* charge. 7 users in the 5–10 band is **7 units at 10% off** — not 4 at full price and 3
discounted.

**Real life.** A bulk crate price, not a tax bracket. Buy 24 and every bottle is cheaper, including
the first.

> **This is the opposite of how meter rate tables work** (§9), which *are* graduated. Two tiering
> systems, two different rules, and the reason to hold them apart in your head.

Bands are only meaningful when a price actually multiplies the item — a flat fee prices one unit,
so a band on it changes nothing. The builder requires **at least two bands**: a single band is just
a different unit price, which the price itself already expresses.

Discounts **truncate** to the minor unit, which makes the reduction very slightly smaller — 5% of
199 takes off 9, not 10. Deliberate, so plans that had bands before automatic discounts existed
keep pricing to the same minor unit.

> ### Test case 3.1 — volume, not graduated
> Price CHF 150 per user. Bands `1–4 → 0%`, `5–10 → 10%`.
> - Quantity 4 → CHF 600.
> - Quantity 5 → **CHF 675** (5 × 150 × 0.9), *not* CHF 735 (4 × 150 + 1 × 135).
>
> Watch for the cliff this creates: 5 users can cost less than 4. That is usually intended (it pulls
> customers up a band) but should be a decision, not a surprise.

---

## 4. Meters

A meter counts events and turns them into an allowance and a bill.

```
meterKey:          screening
displayName:       Screenings
unitLabel:         screening
aggregation:       Sum              ← §5
resetPolicy:       Periodic         ← §6
carryForwardCap:   —                ← §7, only on CarryForward
includedQuantity:  450              ← the free allowance
overageAllowed:    true             ← §9
thresholdPercents: 50, 80, 100      ← §10
rateTables:        CHF bands        ← §9
```

**`includedQuantity` is the only place a plan's free allowance lives.** A rate table never needs a
zero-cost first band to represent it — only the excess (`usage − includedQuantity`) is ever priced.

Usage is recorded through the usage API with a **mandatory idempotency key**: at-least-once delivery
makes a retried call a certainty, and it must not double-count. The ledger behind it is append-only
— a correction is a reversal entry, never an edit, so a bill can always be explained.

> **Keep identifying data out of meter metadata.** Billing needs a count, not a dossier. This is a
> shared billing store retained for years; anything naming a person belongs in your own product's
> records with an opaque reference here.

Every recorded usage call returns the live picture, which is also how you test everything below:

```json
{ "allowed": true, "included": 450, "used": 313, "remaining": 137,
  "overage": 0, "periodKey": "...", "periodStartUtc": "...", "periodEndUtc": "..." }
```

---

## 5. How usage is measured — aggregation

Many events land in one window. This is the rule for collapsing them into the one number that gets
compared against the allowance and priced.

| Option | `+1, +1, +25, +3` → | Means |
| --- | --- | --- |
| **Sum** | 30 | Add up every recording |
| **Max** | 25 | The highest single recording |
| **Last value** | 3 | Only the most recent recording |

### Sum — consumption

Each event happened and cannot un-happen.

**Real life:** the electricity meter, or mobile data. 200 MB Monday plus 300 MB Tuesday is 500 MB,
full stop.

**Use for:** screenings, API calls, emails, SMS, tokens, minutes transcoded, documents processed.

### Max — peak capacity

You are charging for capacity you had to be ready to provide, even briefly.

**Real life:** an industrial electricity *demand charge* — separate from kWh consumed, the utility
bills the single highest spike of the month, because the grid had to be sized for it. Or a hotel
charging on the largest number of guests in the room during a stay.

**Use for:** peak concurrent connections, peak concurrent sessions, peak container count.

**Why not Sum here:** 200 users online in March and 200 in April are mostly the *same people*.
Adding them counts everyone twice.

### Last value — current level

Each recording overwrites the last, because it is a fresh reading of "how much right now".

**Real life:** your bank balance. You do not add up your balances.

**Use for:** GB stored, active integrations, current record count.

**Why not Sum here:** recording `47 GB` then `52 GB` sums to 99 GB, which is a fiction. You never
stored 99 GB.

### Choosing

| The thing you count is… | Choose |
| --- | --- |
| an **action** that happened and is spent | **Sum** |
| a **peak** you had to be sized for | **Max** |
| a **level** you are currently at | **Last value** |

By tense: what they *did* → Sum. The most they *ever had at once* → Max. What they *currently
have* → Last value.

> ### Test case 5.1 — aggregation
> Record `50`, then `200`, then `80`, in one window. Read `used` after each.
> - Sum → 50, 250, 330
> - Max → 50, 200, 200
> - Last value → 50, 200, 80
>
> If your storage meter's `used` climbs monotonically and never falls when customers delete things,
> it is on Sum and should be on Last value.

---

## 6. Allowance resets — reset policy

**Whether** the meter resets, and how it behaves across period boundaries.

| Option | Behaviour | Real life |
| --- | --- | --- |
| **Every allowance period** (`Periodic`) | Spend it or lose it. Resets to zero each window. | A monthly mobile data plan |
| **Never** (`Never`) | One lifetime balance, carried across every renewal | Disk space you occupy |
| **Carry forward** (`CarryForward`) | Resets, but opens with what the last window left, capped | Rollover minutes |

### Core logic

Periods are **derived, never advanced**. A counter's identifier contains the period key, so crossing
a boundary simply addresses a different document, which the next write upserts at zero. There is no
scheduled rollover job, and therefore no possibility of a rollover running twice or not at all.

`Never` addresses a stable `LIFETIME` counter instead, so persistent capacity survives fee renewals
*and* usage-window boundaries. Two consequences:

- **Negative recordings are allowed on a lifetime meter and release capacity** (deleting a file
  gives the GB back). They are **rejected on a periodic meter** — you cannot un-send an email — and
  rejected if they would take a lifetime balance below zero.
- **Only periodic counters are priced.** Monthly rating skips lifetime meters entirely, which is
  why the builder refuses `Never` + overage: *"Lifetime capacity must stop at its allowance; it
  cannot use monthly overage billing."* A lifetime level has no monthly excess to bill.

### When to use what

| You are selling | Policy |
| --- | --- |
| Consumption that refills (screenings, calls, credits) | **Every allowance period** |
| Capacity that stays occupied (storage, retained records) | **Never** |
| Consumption, but unused should not be wasted | **Carry forward** |

> ### Test case 6.1 — the reset actually happens
> `Periodic`, included 450. Record 100 usage in window 1. Roll the clock past the window boundary
> (the simulation harness can advance it) and read the meter.
> → `used: 0, remaining: 450`, and a **new `periodKey`**.
>
> If `used` is still 100, the meter is on `Never`.

> ### Test case 6.2 — lifetime meters release
> `Never` + `LastValue`, included 100 GB. Record `+40`, then `-15`.
> → balance 25. Then record `-30`. → **rejected**, would go below zero.
>
> Repeat both negative recordings against a `Periodic` meter → **rejected outright**.

---

## 7. Carry-forward cap

Appears only on **Carry forward**, and is **required** — the schema refuses to save without it.

**It caps what rolls in, not the total. The included amount is always granted on top.**

With `includedQuantity: 450`, `carryForwardCap: 200`:

```
March   opens with 450             uses 100  →  350 unused
        only 200 may roll ──────────────────┐
April   opens with 200 + 450 = 650  ←───────┘  uses 650  →  0 unused
May     opens with   0 + 450 = 450
```

It applies at **every** rollover, not once. So the ceiling a subscriber can ever hold in one window
is:

```
includedQuantity + carryForwardCap
```

That number — 650 here — is the one to check commercially. With overage at CHF 1.00, a cap of 200
means you have agreed a firm may occasionally consume 650 in a month without paying a franc extra.

**Why it is mandatory.** Without a ceiling a dormant subscription banks allowance indefinitely: a
firm that barely uses the product for eight months arrives at month nine holding 3,600 screenings
and spends them all at once, having paid nothing extra. Every real rollover scheme has a cap for
this reason — rollover data expires, rollover minutes are capped, carried-over vacation days are
capped at five.

### Choosing the number

| Cap | Reads as |
| --- | --- |
| = included | Generous. "One full month of slack." |
| ≈ half of included | The common commercial setting. Smooths lumpy usage, prevents stockpiling. |
| > included | Almost certainly wrong. If a period can carry in more than it grants, the allowance is not really periodic — you may want **Never**. |

> ### Test case 7.1 — the cap binds, the allowance still lands
> Included 450, cap 200. Window 1: use 100 (350 unused). Roll over.
> → Window 2 opens at **650 available**, not 800 (which would be uncapped) and not 200 (which would
> mean the cap replaced the allowance instead of adding to it).

---

## 8. The allowance clock — usage interval

Two fields at the bottom of the pricing step, shown once any meter is not set to `Never`:

```
Allowance resets every:  [Month]   How many: [1]
```

The per-meter dropdown says *whether* it resets. This says *how long a period is*. Month + 1 =
monthly; a quarter is Month + 3.

**Two things surprise people.**

1. **It is plan-level, not per-meter.** One clock, shared by every meter on the plan. You cannot
   give one meter a monthly window and another a weekly one on the same plan.
2. **It is not the billing interval.** This is the whole reason the field exists separately.

### The case it exists for

A yearly plan bills once every 12 months, but the allowance still needs to reset monthly:

```
Price interval:          Year, 1     ← charge once a year
Allowance resets every:  Month, 1    ← 450 screenings back every month
```

The subscription carries both, and the model is explicit that an allowance *"must be described from
this, never from the fee cadence."*

```
Billing period:   ├────────────── one year, one invoice ──────────────┤
Usage windows:    ├─Jan─┤─Feb─┤─Mar─┤─Apr─┤ … ├─Nov─┤─Dec─┤
                    450   450   450   450        450   450
```

So an annual subscriber gets **twelve separate allowances of 450**, not 450 for the year and not
5,400 in one pot. Months 2–12 charge nothing extra: the annual invoice bought access *and* all
twelve allowances. Unused screenings are lost each month unless the meter carries forward.

**Real life.** An annual gym membership with 4 guest passes a month. You pay once in January; in
July you have paid nothing since and still get 4 passes. June's unused passes are gone.

Both values are **snapshotted onto the subscription**, so editing the catalogue later cannot move an
existing subscriber's reset window.

Metering is also **never realigned by calendar billing**. A mid-month signup's allowance stays whole
for the opening stub and nothing is reset or force-rolled on the 1st — *an allowance is capacity for
a period, not money to be prorated*.

> ### Test case 8.1 — the two clocks are independent
> Yearly price, `usageInterval` Month/1, included 450. Subscribe. Advance one month.
> → **No invoice** (the year is not due) **and** the allowance is back to 450 with a new `periodKey`.
>
> If the allowance did not reset, `usageInterval` is following the fee cadence — the 12× under-
> delivery bug, and it does not surface until month two.

---

## 9. Overage and rate tables

`overageAllowed` decides what happens at the allowance:

| `overageAllowed` | Past the allowance |
| --- | --- |
| **false** | Usage is **refused** when the caller sets `enforce`, and rolled back with a compensating entry |
| **true** | Usage continues and the excess is billed from the rate table |

A meter that allows overage and has **no rate table** gives that usage away — it is recorded,
permitted, and billed nothing. The builder says so under the field; it is a warning, not an error,
because a free-for-now meter is a real choice.

### Rate tables are graduated

Unlike volume bands (§3), meter tiers are **graduated/progressive**: each portion of the excess is
charged at its own band's rate, with tier boundaries counted **from the first overage unit,
inclusive**.

```
1–500    excess → CHF 1.00
501–2000 excess → CHF 0.95
2001–5000       → CHF 0.90
5001+           → CHF 0.85
```

600 excess screenings = `500 × 1.00 + 100 × 0.95` = **CHF 595.00**, not 600 × 0.95 = 570.

The last band is always unbounded — one with an upper limit would leave usage past it unpriced.

One rate table per currency. A meter with no rate table **in the subscription's own currency** rates
to zero rather than blocking every other meter's charge over one misconfigured plan.

### Overage is a second, independent invoice

This is the part with real operational consequences:

- It is rated on the **usage clock**, not the fee clock. An annual subscriber who goes over in March
  is charged in March, not next January.
- A decline retries on its **own** bounded schedule (`UsageRatingMaxAttempts` / `UsageRatingRetryHours`)
  and is then abandoned — never charged again, never retried further.
- **It never touches the subscription's `Status`.** A customer whose card is declined for last
  month's overage keeps everything the fee renewal already paid for.
- A missed sweep (worker downtime) closes **every** intervening period, not just the most recent.
- The price's **automatic discount reaches an overage invoice** too. A **volume band does not** — it
  prices units of a quantity item and a meter has none. A **promotional code never** reaches usage
  invoices at all.

> ### Test case 9.1 — graduated, and counted from the first overage unit
> Included 450, bands as above. Record 1,050 total usage (600 excess). Preview the overage.
> → **CHF 595.00**. If you get 570, the bands are being applied as slabs; if you get 1,050 × rate,
> `includedQuantity` is being ignored.

> ### Test case 9.2 — blocked vs billed
> Same meter with `overageAllowed: false` and no rate table. Record usage up to 450, then one more
> with `enforce: true`.
> → `allowed: false`, balance unchanged at 450.
> With `enforce: false` → the call succeeds and the unit is recorded but bills nothing.

> ### Test case 9.3 — the overage decline is contained
> Force an overage charge to decline. → An abandoned usage invoice after its retries, and the
> subscription still `Active`. If the subscription goes `PastDue`, something has wired usage
> failures into fee dunning, which is explicitly not the design.

---

## 10. Notification thresholds

Percentages of the allowance at which the subscriber should be warned — typically 50, 80, 100.

These drive "you have used 80% of your screenings" messaging. They cost nothing and are the
cheapest way to reduce overage bill-shock complaints, so set them on any metered plan where going
over costs money.

---

## 11. What the plan grants — entitlements

Two steps, two different questions:

- **Pricing model** → *what does it cost?* Quantity items and meters. Everything here reaches an
  invoice.
- **What the plan grants** → *may they?* Entitlements. Your application asks these at runtime. **No
  money is involved.**

```
canUse("advanced-reports")  →  allowed / not
canUse("screening")         →  allowed, 137 remaining
```

### The three kinds

| Kind | Carries | Use for |
| --- | --- | --- |
| **Boolean** | nothing | A feature switch — "advanced reports", "SSO", "API access" |
| **Count** | a `limit` **and** a `meterKey` | A numeric limit drawn from a meter |
| **Unlimited** | nothing | Always granted, never counted |

A `Count` entitlement **requires both** — the schema rejects one without a limit and a meter.

### Advisory vs enforcement — the distinction that matters

**Reading an entitlement is advisory. Recording usage is enforcement.**

Two callers at 499 of 500 will *both* be told they have one left. The authoritative answer is the
balance returned by the usage call, which already includes the caller's own contribution — so the
two get different answers and only one of them is over.

A caller that must not exceed an allowance sets `enforce` on the usage call and acts on `allowed`.
A refused call is rolled back with a compensating entry.

The entitlement read touches **only our own database** — no provider gateway, no HTTP client. That
is the guarantee: **if the payment provider is down, every existing customer keeps working.** The
subscription is cached briefly; **usage counters are never cached**, because a stale one would let a
caller past an allowance already spent.

### Do you need one at all?

Billing works fine without any entitlement — the meter counts, resets, and bills on its own, and
every usage call returns the full balance.

What you lose is the ability to **ask** without spending. `getEntitlement("screening")` on a plan
with no such entitlement returns `NotInPlan`. So the only way to learn the balance is to record a
screening — useless for:

- a dashboard KPI card showing remaining balance (a page load is not a screening)
- a "you are running low" banner
- a pre-flight check before a bulk run
- a disabled button

**Rule:** add an entitlement when something *other than the usage call itself* needs the answer.

### Limit vs included — they may differ, on purpose

Nothing forces the entitlement's `limit` to equal the meter's `includedQuantity`, and the three
combinations are three different products:

| Config | Result |
| --- | --- |
| limit 450 = included 450 | Permission and billing agree. The plain case. |
| limit 500 > included 450, **overage allowed** | The app reports all 500 allowed; 50 are billed as overage. **A real, intentional configuration.** |
| limit 500 > included 450, **overage blocked** | The plan promises 500, the meter refuses at 450, *the last 50 can never be used*. A bug. |

The builder **reports** the mismatch rather than rejecting it, because the middle row is legitimate.

> ### Test case 11.1 — the entitlement is readable without spending
> Add `screening` as `Count`, limit 450, meter `screening`. Record 313 usage. Then read entitlements
> **without** recording anything.
> → `allowed: true, used: 313, remaining: 137`, and `used` is still 313 afterwards.

> ### Test case 11.2 — advisory really is advisory
> At 449 of 450, read the entitlement twice concurrently. → **both** say allowed. Then record usage
> twice with `enforce: true`. → exactly **one** succeeds. This is correct, not a race bug.

---

## 12. Prices, and what they multiply

A price is `unit amount × quantity`, on an interval. The **"What this multiplies"** dropdown decides
what quantity means:

| Selection | Stored | 1 unit pays | 7 units pay |
| --- | --- | --- | --- |
| **Flat fee** | `quantityItemKey: null` | 150 | **150** |
| **Per user** | `quantityItemKey: "user"` | 150 | **1,050** |

The dropdown is populated from the quantity items you defined in the same step — that is the link
between the two halves of the pricing page, and the one people miss.

A plan can carry several prices: one per currency, one per cadence (monthly and yearly), each with
its own alignment, discount and tax. Two prices with **identical terms** (same currency, interval,
interval count and quantity item) are rejected — the server refuses the duplicate *after* creating
the plan, so the builder catches it first.

**Prices are immutable in their commercial terms.** Tax metadata and the automatic discount can be
updated for *future* subscriptions; the amount, interval and alignment cannot change. To reprice,
add a new price and retire the old one. Existing subscribers keep the terms they were sold either
way, because a subscription bills from its own snapshot and is never migrated automatically.

**Editing a plan ends when the first subscriber arrives.** After that the plan is frozen — a
subscription bills from its own copy of the terms, which an edit cannot reach.

> ### Test case 12.1 — flat fee ignores quantity
> Flat-fee price CHF 950. Set quantity to 1, then 6, then 9, previewing each.
> → **CHF 950 every time**, and the prorated charge for each change is **0**.
> Switch the price to "Per user" and repeat → 950 / 5,700 / 8,550.

---

## 13. Billing alignment

Chosen per price, snapshotted onto every subscription sold on it, and **cannot be changed
afterwards**.

| Option | Renews on |
| --- | --- |
| **Anniversary** (default) | The day they signed up — 25 August → 25 September |
| **Calendar month** | The 1st, after a prorated opening period |

**Only `Month × 1` or `Year × 1` may be calendar-aligned.** A quarterly price has no single "first"
that is not also a choice of which month, so the combination is refused at authoring time rather
than guessed at on an invoice.

The two cadences anchor differently, and the difference *is* the yearly feature:

| | Anchors on | Opening period | Then |
| --- | --- | --- | --- |
| `Month × 1` | the 1st of the month it starts in | the rest of that month, prorated | the 1st, monthly |
| `Year × 1` | the 1st of the month **after** | the rest of that month, prorated | that same 1st, yearly |

A year anchored on the month it *started* in would end on 1 August after a 25 August signup —
eleven months for a year's money — and no later boundary could correct it, since every one is
derived from the anchor.

### The opening stub

A signup on 25 August gets `[25 August, 1 September)` and pays **7/31** of the monthly amount: the
25th through the 31st is seven calendar dates counted inclusively, over the 31 the month actually
has. February uses 28 or 29 as appropriate.

Two consequences worth knowing:

- **Time of day never enters into it.** Everyone signing up on the 25th buys the same seven dates
  and pays the same fraction. Otherwise a 23:59 signup would pay for a day it had a minute of.
- **The subscriber's calendar decides, not the server's.** 31 August 23:00 UTC is already
  1 September in Zurich, so a Zurich subscriber signing up then gets a whole month, not a one-day
  stub — and is not reported as prorated at all.

Month ends **clamp on read, never on write**: an anchor on the 31st bills on the 28th in February
and returns to the 31st in March. Daylight saving is resolved rather than thrown — a boundary inside
a spring-forward gap moves to the first instant that exists.

> ### Test case 13.1 — calendar proration uses real days
> Calendar-aligned monthly price CHF 950. Subscribe 25 August.
> → Opening charge **CHF 214.52** (`95000 × 7/31`), next renewal 1 September at CHF 950.
> Repeat on 25 February (non-leap): `95000 × 4/28` = **CHF 135.71**. A fixed-30-day assumption gives
> 126.67 and is wrong.

> ### Test case 13.2 — signing up on the 1st is not a stub
> Subscribe 1 September in the subscriber's own time zone. → A **full** period, `prorated: false`.

---

## 14. Calendar-aligned yearly

Two extra fields appear, and both are required to be **absent** on any other kind of price.

### Stub base price

A monthly stub is a fraction of the price being charged. A yearly one cannot be — a week of an
annual amount is not a quantity anybody can charge. So a calendar-aligned yearly price must **name
the monthly price its opening period is a fraction of**.

The referenced price is validated at authoring time: same plan, active, `Month × 1`, same currency,
same quantity item, same tax rate and mode. Each of those, left to differ, produces two figures a
subscriber cannot reconcile and only discovers on an invoice.

**The annual amount stays independently authored.** What a year costs is a commercial decision — an
annual plan is usually not twelve monthly ones — and deriving it would take that decision away from
whoever is selling it.

Worked through at CHF 950/month and CHF 11,400/year with 8% off for paying annually, signing up
25 August:

| | Calculation | Amount |
| --- | --- | --- |
| 25–31 August | `95000 × 7/31` = 21452, less 8% | **CHF 197.36** |
| 1 September | `1140000`, less 8% | **CHF 10,488.00** |
| 1 September next year | the same again | **CHF 10,488.00** |

The yearly price's automatic discount and volume band apply to the stub as well as the year —
somebody who buys an 8%-off annual plan on the 25th is on that plan from the 25th, and charging them
undiscounted for the first week would be selling them the discount a week late.

**A promotional code is the exception: it applies to the year alone**, and is consumed once when the
year settles. Spending a month of a three-month promotion on a seven-day stub would exchange a month
of discount for a week of it.

### When the year is collected

| | At checkout | On 1 September | Cancelling during the stub |
| --- | --- | --- | --- |
| **At boundary** (default) | the stub | the year is charged | access ends with the stub; **the year is never charged** |
| **Up front, with the first payment** | the stub **and** the year | the year opens, nothing charged | **nothing is refunded**; access runs to the end of the year |

Both come to the same money. **An author choosing between these is choosing a refund policy as much
as a collection date**, which is why the builder states both consequences.

Between signup and the 1st the subscription carries a pending annual period whose figures are
**frozen at checkout creation and never recalculated at the boundary** — that boundary is a month
away, and a charge that re-derived its own amount could take a different sum than the one the
subscriber agreed to.

While that year is **unpaid**, a plan or quantity change is refused: repricing before the opening
charge clears would silently discard a charge about to be collected. A downgrade or decrease can
still be *scheduled* for the boundary. Once the year is **prepaid**, a change that keeps the
cadence and boundary — a compatible upgrade, or any quantity increase — settles the stub's remaining
days and the paid year together in one immediate charge.

> ### Test case 14.1 — the stub is priced from the monthly basis
> Yearly CHF 11,400 with 8% off, stub base CHF 950. Subscribe 25 August.
> → Stub **CHF 197.36**, not `1140000 × 7/365` (≈ CHF 201) and not an undiscounted 214.52.

> ### Test case 14.2 — the two timings differ only in refund exposure
> Author the same plan twice, once per timing. Subscribe 25 August, cancel 28 August.
> - At boundary → charged the stub only; the year never happens.
> - Up front → charged stub + year; **nothing refunded**, access runs a full year.

---

## 15. Automatic discounts

A price can reduce itself, with no code redeemed and no subscriber action — the mechanism behind
"8% off if you pay yearly".

It sits on the **price, not the plan**, because the offer is cadence-specific: the yearly price
carries it and the monthly price beside it does not. Two prices, one plan, one product.

**Prefer authoring the yearly price as the full amount plus an 8% automatic discount** rather than
as a pre-discounted figure. The derivation stays visible and adjustable instead of being baked into
a number nobody can trace.

### Three reductions, two combination settings

They answer two different questions, which is why collapsing them into one policy would make a
cadence discount negotiate with a coupon.

**1. Price-level: automatic discount vs volume band** (`quantityDiscountCombination`)

| Option | 8% automatic + 5% band |
| --- | --- |
| **Best discount** | whichever removes more **money**, and only that one |
| **Additive** | rates added and applied once → **13%** (not the 12.6% sequential would give) |

Capped at 100% — two generous rates must not arrive at a negative charge.

**2. Plan-level: built-in reductions vs a redeemed code** (`quantityDiscountCombinationPolicy`)

| Option | Means |
| --- | --- |
| **Best discount** | the code competes; the larger reduction wins |
| **Quantity only** | built-in discounts only; a code adds nothing |
| **Stack** | they compound |

**A promotion that lost is not consumed** — losing to an automatic discount counts as losing.

### Order of operations

The gross is scaled once, at the front, and everything downstream applies to a prorated gross:

1. Gross from the price and its quantities
2. **Prorate by covered calendar days** (nearest minor unit, halves away from zero)
3. Built-in reductions — automatic discount and volume band — per the price's combination
4. Promotional code, per the plan's policy. A *fixed* code is prorated by the same day fraction; a
   percentage needs no scaling
5. Tax, on the discounted amount
6. Banked credit, last of all — it pays the bill rather than changing what the bill was for

**Steps 3–4 must come after proration, not before.** A discount worked out against a whole month and
then subtracted from a fraction of one is not a smaller discount, it is a larger one — 8% of a full
month against a seven-day stub would take a third of the stub. A fixed discount left whole would
make a small enough stub free.

A successfully paid **stub counts as one period** against a limited-duration promotion: three months
of "20% off" that skipped the stub would run to four bills.

> ### Test case 15.1 — additive vs best
> Price CHF 1,000, automatic 8%, quantity in a 5% band.
> - Best discount → CHF 920 (the 8% wins)
> - Additive → CHF 870 (13%), **not** CHF 874 (sequential)

---

## 16. Tax

Per price, in basis points, with a mode:

| Mode | Means |
| --- | --- |
| **Exclusive** | Tax is added on top of the amount |
| **Inclusive** | The amount already contains the tax |

Tax is calculated at step 5 — **on the discounted amount**, never on the gross. Any price authored
before modes existed is reported and charged as exclusive.

---

## 17. Trials

### Duration kind

| Kind | Count | Ends at |
| --- | --- | --- |
| **Days** | 1–365 | `count × 24 hours` after signup — a fixed span, never converted through a time zone |
| **End of calendar month** | none | Local midnight on the 1st of the month after signup |
| **Anniversary months** | 1–12 | The same local wall-clock time `count` months later, clamped when the day does not exist (31 January + 1 month → 28/29 February) |

The last two resolve in the **subscription's own time zone**, then freeze as UTC.

**The trial end is an exclusive boundary.** A trial resolving to local midnight on 1 September has
run *through* 31 August. A UI showing that boundary should say **"through August 31"**, not "ends
September 1", which reads as a day later than it is.

Because *End of calendar month* is anchored to the calendar rather than a span, **a signup late in
the month gets a short trial** — 31 August grants only until 1 September, by design. Nothing tops it
up to a minimum length. If that is unacceptable, use `Days` or `AnniversaryMonths`.

All trial terms are **frozen at creation**. Editing a plan's trial rule changes nothing for anyone
already on it.

### Require a card to start the trial

| Setting | Behaviour |
| --- | --- |
| **On** | A card is saved now **without a charge**. The first paid period is charged when the trial ends. |
| **Off** | The trial starts with no card. A payment method is required before paid access can continue. |

Requiring a card and charging for the first period used to be the same act. Card setup separated
them: the card is stored by a setup session that charges nothing.

### What happens at trial end

| | Result |
| --- | --- |
| Card on file | Charged; → `Active` |
| **No card on file** | → **`Unpaid` immediately, no retries, no grace period** |

That last row is the one to internalise. A subscription with no stored payment method **skips
dunning entirely** — retrying a charge with nothing to charge cannot succeed on attempt two any more
than on attempt one. There is no `PastDue` grace window for a card-free trial. `Unpaid` does not
grant entitlements, so access stops.

Recovery is the "add payment method" action, which opens a card-collection session. What happens
once the card is stored depends on status, and the server decides: **nothing** for a trial in
progress, an **immediate charge** for a recovering `Unpaid` subscription.

### Trial conversion and calendar boundaries

- A **payment-free** trial ending mid-month charges a stub from the trial-end date to the next 1st,
  keyed to the period the **trial ended in** — never the sweep's own clock. A conversion discovered
  late (trial ended 20 August, nothing picked it up until 2 September) still bills the 12/31 August
  stub it owes, then raises September separately. Anchoring on the clock would silently write off
  the days in between.
- A trial ending **on the 1st** starts with a full period.
- A **payment-required** trial is charged up front at checkout, so its first fee uses the calendar
  stub exactly as an ordinary signup does.
- The trial only decides **when** the first charge happens, not which boundaries the price renews on.

> ### Test case 17.1 — the exclusive boundary
> `EndOfCalendarMonth` trial, signup 12 August, subscriber in Europe/Zurich.
> → `trialEndsAtUtc` is local midnight 1 September. Confirm the UI says **"through August 31"**.

> ### Test case 17.2 — card-free trial end
> Trial with the card requirement **off**. Let it end without adding a card.
> → **`Unpaid` immediately**. Not `PastDue`, and no retry attempts in the audit trail. Entitlements
> stop resolving. Then add a payment method → the outstanding amount is charged straight away.

> ### Test case 17.3 — short trial by design
> `EndOfCalendarMonth`, signup 31 August. → Trial ends 1 September: **one day**. Expected. If you
> need a minimum length, this is the wrong kind.

---

## 18. Trial grants

A trial can have its **own allowance** for a meter instead of the plan's normal one.

**Use it when the regular allowance would be an open invitation to sign up, consume and leave.** A
plan including 2,000 screenings a month should not hand all 2,000 to a free trial.

A **lifetime meter cannot have a trial grant** — the builder rejects it. A lifetime balance has no
separate trial window to grant into.

> ### Test case 18.1
> Plan includes 450; trial grant 50. Subscribe onto the trial → allowance reads **50**. Let the
> trial convert → allowance reads **450** in the first paid window.

---

## 19. Payment method upfront

> "Require a payment method before activation, even when nothing is due today"

This is **not** the trial's card question, and the two are not redundant. The trial setting scopes to
a trial; this one governs **activation**, including a plan with no trial at all.

### The three-way fork at signup

| Opening amount | Card required | What happens |
| --- | --- | --- |
| more than zero | — | the ordinary payment checkout |
| zero | **no** | activates immediately, no checkout |
| zero | **yes** | a **card-setup** checkout; stays `Incomplete` until the card is stored |

A card is required when the amount is zero and **either** the plan sets this **or** the subscription
starts on a trial that requires one. Both together is the combination the setting exists for:
genuinely free until the trial ends, with a card on file so the charge that ends it has something to
bill.

### "Nothing due today" without a trial

This is why the setting is separate. A trial is only one reason the opening charge is zero:

1. **A 100% promotional discount** on the first period. No trial anywhere; the first invoice is
   simply zero.
2. **A calendar stub that rounds to nothing** — signing up on the last day of a month.
3. **A genuinely zero price** for a free tier that converts later.

In all three, without this flag the subscription activates with **no card on file** — *"which is a
problem only if there will be a later one."* If your product always has a later one, turn it on.

### What the setup actually is

A hosted checkout session in **setup mode** — not a one-cent charge (which appears on a statement
and must be refunded) and not a zero-value payment (which providers reject). It produces the
off-session mandate the first renewal relies on.

It leaves behind a zero-amount record that is **not a payment**: excluded from payment listings,
refunds, captures and invoice history.

Two things behave differently from a charge:

- **Failure is not fatal.** A declined charge ends the subscription; nothing was refused here, so it
  stays `Incomplete` and another attempt is free to succeed.
- **An expired session is replaced**, not retried — a hosted session cannot be reopened. An expired
  *charge* is still a conflict, because raising a second one is how the same money gets taken twice.

Cancelling while a setup is outstanding settles it, so completing the card form afterwards cannot
start a subscription somebody has cancelled.

> ### Test case 19.1 — zero today, card held
> Plan with a 100% first-period promotion, setting **on**. Subscribe.
> → A **setup** checkout, subscription `Incomplete`, **no charge on the statement**. Complete it →
> `Active` with a card on file. Abandon it → stays `Incomplete`; retrying mints a **new** session.

---

## 20. Plan families, archiving, and Tier 5

### Families

`familyCode` + `familyRank` (supplied together or not at all) tie tiers into one ladder, so
"upgrade" and "downgrade" have a defined meaning across the set. Rank 1..4 for four tiers.

### Archiving

**Archiving is permanent and means exactly one thing: nothing new can be sold on the plan.**
Everyone already subscribed keeps renewing, rating usage and granting entitlements exactly as
before, because they bill from their own snapshot. It removes the plan from subscribe and
change-plan selectors without any screen filtering anything itself.

You may also name a **predecessor** plan when creating one — a display-only link. It never moves a
subscriber and never changes either plan's editability.

### Custom/enterprise tiers

A tier that bypasses the public flow should not be in the public catalogue. Plans can be **scoped to
a single organization**, so an enterprise tier is *one privately-scoped plan per customer*, created
after terms are agreed. The public catalogue shows a "contact sales" card that is not a plan at all.

---

## 21. The complete worked plan

Tier 2 in full. The other tiers are identical but for four numbers.

```
IDENTITY
  code                     TIER-2
  displayName              Tier 2
  familyCode / familyRank  professional / 2
  description              "For firms of 4–9 users"     ← marketing range lives here

PRICING MODEL
  Quantity item
    itemKey                user
    unitLabel              user
    min / max / default    1 / 9 / 1        ← max enforced; min deliberately 1
    volume bands           none

  Meter
    meterKey               screening
    unitLabel              screening
    aggregation            Sum
    resetPolicy            Every allowance period
    includedQuantity       450
    overageAllowed         true
    thresholdPercents      50, 80, 100
    rateTable (CHF)        ≤500 → 1.00 | ≤2000 → 0.95 | ≤5000 → 0.90 | above → 0.85

  Allowance resets every   Month × 1        ← stays monthly even on the yearly price

  Prices
    CHF 950     Month × 1   Flat fee   alignment CalendarMonth
    CHF 11,400  Year  × 1   Flat fee   alignment CalendarMonth
                automatic discount 8%
                stub base price = the CHF 950 monthly price
                annual charge timing = At boundary

  Payment method           require upfront: on

WHAT THE PLAN GRANTS
  screening                Count, limit 450, meter screening

TRIAL
  (none on the standard tiers)
```

Per-tier differences only:

| | Tier 1 | Tier 2 | Tier 3 | Tier 4 |
| --- | --- | --- | --- | --- |
| `familyRank` | 1 | 2 | 3 | 4 |
| user max | 3 | 9 | 24 | 40 |
| monthly | 290 | 950 | 2,500 | 5,000 |
| yearly base | 3,480 | 11,400 | 30,000 | 60,000 |
| included screenings | 150 | 450 | 1,200 | 2,000 |
| rate table | identical across all four | | | |

Build Tier 1 completely, run the test cases against it, then clone.

---

## 22. Authoring mistakes that pass validation

Every one of these saves cleanly and is wrong.

| Mistake | Symptom | Fix |
| --- | --- | --- |
| Marketing minimum in `minQuantity` | Small firms cannot buy the tier they want | `minQuantity: 1`; range in the description |
| Per-unit price on a flat tier | Fee rises as users are added | "What this multiplies" → **Flat fee** |
| `usageInterval` left matching the fee cadence on a yearly plan | Annual subscribers get one allowance for the whole year — a 12× under-delivery, invisible until month two | Month × 1 |
| Meter allows overage, no rate table | Overage recorded, permitted, **billed nothing** | Add a rate table in every currency you sell |
| Zero-cost first band "to represent the allowance" | Double-counts the allowance | Only the excess is priced; `includedQuantity` is the allowance |
| Storage meter on `Sum` | Usage only ever climbs; deletions do nothing | `LastValue` + `Never` |
| Entitlement limit above `includedQuantity` with overage blocked | Customers see a permission they can never exercise | Match the limit, or allow overage |
| No entitlement on a metered plan | Dashboards cannot show a balance without spending one | Add a `Count` entitlement |
| Carry-forward cap larger than the included amount | The allowance is not really periodic | Halve it, or use `Never` |
| Yearly price authored as the pre-discounted figure | Nobody can trace where the number came from | Full amount + automatic discount |
| Card requirement off on a converting free trial | Every trialist lands in `Unpaid` at conversion — no grace, no retries | Turn it on |
| Expecting `PastDue` to protect a card-free trial | It does not; there is nothing to retry | See §17 |

### The two rules worth memorising

> **Volume bands are volume. Meter rate tables are graduated.**
> 7 seats in a 5–10 band → all 7 discounted. 600 excess units → 500 at one rate, 100 at the next.

> **The fee clock and the usage clock are independent, and so are their failures.**
> A declined overage charge never changes the subscription's status. A declined renewal never
> touches the allowance.
