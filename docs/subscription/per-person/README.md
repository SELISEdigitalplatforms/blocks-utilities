# Per-person plans (user-wise subscriptions)

A plan can be sold to **the organization** as a whole, or to **each person** in it. This guide covers
the second kind end to end: what it means, what the plan builder asks for, every rule the server
applies, every endpoint involved with its payload and response, who may call what, and how a client
application consumes it.

For how any of it is *implemented*, the authority is
[`server/Subscription.DomainService/README.md`](../../../server/Subscription.DomainService/README.md).
Where the two disagree, that one is right and this one is a bug.

---

## Contents

1. [The 60-second version](#1-the-60-second-version)
2. [What means what](#2-what-means-what)
3. [The cases it covers](#3-the-cases-it-covers)
4. [Business rules](#4-business-rules)
5. [What changed in the plan builder](#5-what-changed-in-the-plan-builder)
6. [Money: what is charged when](#6-money-what-is-charged-when)
7. [API reference](#7-api-reference)
8. [Which API to call when](#8-which-api-to-call-when)
9. [How a client consumes it](#9-how-a-client-consumes-it)
10. [Permissions](#10-permissions)
11. [Error codes](#11-error-codes)
12. [Known gaps](#12-known-gaps)

---

## 1. The 60-second version

| | Organization plan | Per-person plan |
| --- | --- | --- |
| Who it is sold to | The organization, as one thing | The organization buys **places**; an administrator gives each place to one **person** |
| How many per organization | One live subscription | Any number, **beside** the organization's own |
| Allowance | One pool everybody draws on | **Each place has its own**, e.g. 100,000 tokens per place per month |
| Who spends it | Anyone in the organization | Only the person holding the place |
| Overage | Usage past the pool | Usage past **each place's own** allowance, added up |
| Found with | `GET /api/subscriptions/current` | `GET /api/subscriptions/member-based` (administrator) or `GET /api/subscriptions/mine` (the person) |

The two kinds are **not alternatives**. An organization commonly holds both: an organization plan for
what everyone shares (projects, storage, workspaces) and a per-person plan for what each person uses
on their own (AI tokens, calls, seats in a tool). When a person uses something, **their place answers
first**; anything their place doesn't cover falls through to the organization's plan.

> **One place = one person = one allowance.**
> The place, not the person, owns the allowance. A place keeps what it has spent when it changes
> hands, so the next holder inherits the rest of that period.

---

## 2. What means what

| Term | Meaning | Where you see it |
| --- | --- | --- |
| **Subscriber scope** | Who a plan is sold to. `0` = Organization, `1` = User (each person). Chosen when the plan is created and **fixed after**. | Plan builder step 1; `subscriberScope` on the plan |
| **Per-person / user-wise / member-based** | Three names for the same thing: a subscription on a `subscriberScope: 1` plan. | "Per person" badge; `…/member-based` |
| **Place** (a.k.a. **seat**) | One slot on a per-person subscription, numbered from 1. Carries its own allowance and usage. | `seatNumber` |
| **Holder** / **member** | The person currently given a place. At most one per place. | `userId` on a seat |
| **Counting quantity** | The quantity item whose number is how many places there are. The only item when the plan sells one; otherwise the one marked **"This quantity counts people"**. | `countsMembers: true` |
| **Priced per place** | The price multiplies the counting quantity: places bought = quantity bought. | a price whose `quantityItemKey` is the counting item |
| **Flat-priced** | The price doesn't multiply the counting quantity: places = the item's **maximum**. | a price with no `quantityItemKey`, or on another item |
| **Purchased** | Places the subscription was bought with. | `purchased` |
| **Held** | Places someone is on right now. | `held` |
| **Scheduled** | Places left after a decrease booked for the period end. | `scheduledPlaces`, `scheduledAtUtc` |
| **Available** | Places that can be filled today: the smaller of purchased and scheduled, minus held. | `available` |
| **Place allowance** | The meter's `includedQuantity`, given to **every place** separately. | `included` on `…/usage/mine` or `usage[]` |
| **Pace** (sub-limit) | A short-window cap inside the allowance ("at most 1,000 an hour"). On a per-person plan it applies **per place**. | `subLimits` |
| **Release** | Taking a person off their place. The place's usage stays. | `DELETE …/members/{userId}` |
| **Mine** | Answers for the signed-in person: their places, and what they may spend. | `GET /api/subscriptions/mine`, `GET /api/subscription-usage/mine` |

---

## 3. The cases it covers

| # | Case | What happens |
| --- | --- | --- |
| 1 | An organization subscribes to a per-person plan while already holding an organization plan | Allowed. The preview doesn't report "already has a live subscription" for a per-person plan. |
| 2 | An organization subscribes to two different per-person plans | Allowed; both are listed by `member-based`. |
| 3 | An administrator assigns 10 people to a 5-place subscription in one call | 5 assigned, 5 refused with `subscription_member_limit_reached`. The call succeeds; the answer is per person. |
| 4 | Someone already on the subscription is named again | Refused with `subscription_member_already_assigned`, before the capacity check, so it's never reported as "out of places". |
| 5 | A person on plan A (meters `token`) is assigned to plan B, which also meters `token` | Refused with `subscription_member_meter_overlap`. A use of `token` couldn't tell which place to draw on. |
| 6 | Two administrators fill the last place at the same moment | One assignment and one refusal; the database settles the race. |
| 7 | Which free place a newcomer gets | The free place with the **most allowance left** (summed across meters); ties go to the lowest number. A newcomer never gets a half-spent place while an untouched one sits empty. |
| 8 | A person is released mid-period and someone else is assigned | The place keeps its usage; the new holder inherits what's left of that period's allowance. |
| 9 | A person uses a meter their place covers | Drawn from **their place**. |
| 10 | A person uses a meter their place doesn't cover | Drawn from the **organization's own** plan, if it meters it; otherwise refused as not on the plan. |
| 11 | A person with no place uses a per-person meter | Only the organization's plan can answer. If it doesn't meter that key, the use is refused. |
| 12 | A place goes past its allowance | Recorded as overage for that place (or refused, if the caller sent `enforce: true` or the meter doesn't allow overage). |
| 13 | A place goes past its pace | `Refuse` pace: the use is refused and nothing is counted. `Throttle` pace: allowed, with `subLimitExceeded: true` so the caller can slow down. |
| 14 | Place 1 is 30 over, place 2 is 40 under | Overage billed = **30**. Place 2's leftover doesn't offset place 1's excess. |
| 15 | Places are added mid-period | Charged immediately, prorated for the rest of the period. The new places can be filled at once. |
| 16 | Places are removed | Scheduled for the period end. Places above the new number can't be filled in the meantime. |
| 17 | A decrease would leave fewer places than people on them | Refused with `subscription_member_seats_occupied` ("2 people have to come off first"). Nobody is released automatically. |
| 18 | Upgrade to another per-person plan | Immediate, prorated; places and holders carry over; each place starts the new plan's allowance. |
| 19 | Monthly ↔ yearly on the same plan | Monthly → yearly is immediate; yearly → monthly is scheduled for the period end. |
| 20 | Change from a per-person plan to an organization plan, or back | Refused with `subscription_plan_change_scope_mismatch`. Subscribe to the other plan separately. |
| 21 | Cancel at period end | Places stay held and keep working until the period ends; then everyone is released. It can be withdrawn until then. |
| 22 | Cancel immediately | Status becomes `Canceled` and every place is released at once. The period's overage is still rated and invoiced. |
| 23 | A person leaves after the subscription has lapsed | They can still be released; listing still works too. |
| 24 | Assigning on a subscription that no longer grants anything | Refused with `subscription_not_live`. |
| 25 | Calling member endpoints on the organization's own subscription | Refused with `subscription_not_member_based`. |
| 26 | Assigning before the first payment has landed | Refused with `subscription_not_live`. The checkout webhook, not the browser returning, activates the subscription. |

---

## 4. Business rules

**Places**

1. **How many places.** Priced per place: the counting quantity bought. Flat-priced: the counting
   item's maximum. A plan selling no quantity at all has exactly one place.
2. **Which item counts people.** The only quantity item when there's one. When there are several,
   exactly one must be marked `countsMembers`. The builder and the server both refuse any other count.
3. **A flat-priced per-person plan needs a maximum** on its counting item, or there's no number of
   places to derive. The builder refuses it; the server can only discover it at assignment
   (`subscription_member_count_ambiguous`).
4. **A place is filled only while the subscription is live.** Listing and releasing work on a lapsed
   subscription; assigning doesn't.
5. **One person, one place per subscription.** A unique index enforces it.
6. **One place per meter across subscriptions.** A person can hold places on several per-person
   subscriptions only if no two of those plans meter the same key.
7. **Newcomers get the place with the most allowance left**, measured by the effective allowance, so
   rolled-over leftovers count.
8. **A decrease never strands anyone.** It's refused while more people hold places than it would
   leave. Once booked, places above the new number can't be filled until it takes effect.

**Usage**

9. **Every place has its own counters**, per meter and per period, starting at zero.
10. **A recording spends the caller's own place first**, then the organization's plan, choosing by
    meter. The same rule answers `GET /api/subscription-usage/mine`, so what it shows is what the next
    use draws down.
11. **Usage belongs to the place, not the person.** Releasing doesn't reset it; the next holder
    inherits it.
12. **Paces apply per place.** A refused use costs no window anything; every pace counter it touched
    is put back.
13. **`enforce: true` is the only real gate.** Reading an entitlement first and deciding is a check,
    not enforcement: two callers at 99 of 100 can both pass it.

**Billing**

14. **Overage is summed place by place.** Each place's overage is what it used past its own allowance.
    The invoice line says so: *"usage beyond each place's own allowance (2 places: 200 included, 190
    used between them)"* (from #619).
15. **Places are the price multiplier.** Adding places charges a prorated difference now; removing
    them takes effect at renewal and isn't refunded.
16. **Plan changes keep the scope.** A per-person subscription can only move to another per-person
    plan.

**Ending**

17. **Everyone is released when a subscription ends**, either at the end of the period for a
    scheduled cancel, or at once for an immediate one. Their usage rows stop naming a holder too
    (from #619).
18. **Access never depends on the release landing.** Entitlement resolves only live subscriptions,
    so a place on an ended one grants nothing either way.

---

## 5. What changed in the plan builder

| Where | What | Rule |
| --- | --- | --- |
| **Step 1 — Identity** | New question **"Who is this plan for?"**: *The organization* (one subscription everybody shares) or *Each person* (the organization buys places and gives one to each person; every place gets its own allowance). | Fixed once the plan exists. The server ignores a scope change on edit. |
| **Step 2 — Pricing model** | On a per-person plan, an explanation of how places will be counted, worked out from the prices authored so far: *per place*, *flat*, or *not known yet*. | — |
| **Step 2 — Quantity items** | On a per-person plan selling **more than one** quantity, each item gets a **"This quantity counts people"** checkbox. | Exactly one must be ticked (`subscription_plan_member_quantity_ambiguous`). |
| **Step 2 — Quantity items** | A flat-priced per-person plan must give its counting item a **Max**. | *"A flat-priced plan needs a maximum, or there is no ceiling to derive."* |
| **Step 3 — Meters** | Unchanged fields, new meaning: **Included quantity** is given to **each place**. Paces (sub-limits) are also per place. | — |
| **Review / summary** | A **Per person** badge, and one sentence with the plan's whole shape, e.g. *"10 places; 100,000 tokens each, at most 1,000 an hour."* | — |
| **Plan catalogue** | The "Per person" badge on the plan card. | — |

**Authoring checklist for a per-person plan**

1. Choose **Each person** in step 1.
2. Add one quantity item for the places (e.g. `seat`, unit label `seat`), usually with minimum 1.
3. Price it **per seat** (price multiplier = that item) so buying 5 means 5 places.
4. Add each meter with the allowance **one person** gets.
5. Optionally add paces per meter, and overage rates.
6. Check the review sentence reads as you intend.

A plan metering the same key as another per-person plan is allowed. But nobody can then hold a place
on both at once (rule 6), so give distinct products distinct meter keys.

---

## 6. Money: what is charged when

Example plan: USD 10 per place per month + 8.1% tax, 100 tokens per place, overage USD 0.50 per token.

| Event | When it's charged | Example |
| --- | --- | --- |
| Subscribe with 2 places | At checkout, for the first period | 2 × $10 + tax = **$21.62** |
| Add places (2 → 5) mid-period | Now, prorated for the rest of the period | ≈ 3 × $10 × days left ÷ days in period, + tax |
| Remove places (5 → 4) | Nothing now; the renewal is priced at 4 | Next renewal 4 × $10 + tax |
| Renewal | At the period end, for the places bought (held or not) | 5 × $10 + tax = $54.05 |
| Overage | After the usage period closes, as a **separate invoice** | Place 1 used 130, place 2 used 60 → 30 over → 30 × $0.50 = $15 + tax = **$16.22** |
| Upgrade to another per-person plan | Now: the new period minus what's unused of the old one | 2 places, $10 → $20 plan, day 1: **$21.64** |
| Monthly → yearly | Now, the same way | 2 × $200/yr − unused month: **$389.16** |
| Yearly → monthly | Nothing now; takes effect at the end of the year | — |
| Cancel at period end | Nothing; the paid period runs out | — |
| Cancel immediately | Nothing refunded; the period's overage is still invoiced | — |

Discount codes reduce the subscription fee, not the overage (see [known gaps](#12-known-gaps)).

The figures in the examples come from a live test run on dev (INV-2026-000144 to 000145, and the plan
changes that followed).

---

## 7. API reference

Every endpoint is under `/api`. Every response uses the same envelope:

```json
{
  "success": true,
  "data": { },
  "error": null,
  "meta": { "correlationId": "0HN…:00000007", "timestampUtc": "2026-09-29T17:59:25Z", "replayed": false }
}
```

A failure has `success: false`, `data: null` (or partial data where noted) and
`error: { "code", "message", "fields", "traceId" }`.

`organizationId` (query or body) is **ignored unless the caller is the platform console**. Everyone
else always acts on their own organization, taken from the token.

### 7.1 Plans

#### `GET /api/subscription-plans` — list plans

Permission `subscription-plan::read`. Each plan now carries `subscriberScope` (`0` Organization,
`1` User), and each quantity item carries `countsMembers`.

#### `POST /api/subscription-plans` — create a per-person plan

Permission `subscription-plan::manage`.

```json
{
  "code": "ai-seat",
  "displayName": "AI assistant",
  "subscriberScope": 1,
  "usageInterval": 2,
  "usageIntervalCount": 1,
  "quantityItems": [
    { "itemKey": "seat", "unitLabel": "seat", "minQuantity": 1, "maxQuantity": null, "defaultQuantity": 1, "countsMembers": true }
  ],
  "meters": [
    {
      "meterKey": "token", "displayName": "Tokens", "unitLabel": "token",
      "aggregation": 0, "resetPolicy": 0, "quantityScale": 0,
      "includedQuantity": 100000, "overageAllowed": true,
      "subLimits": [ { "window": 0, "windowCount": 1, "rolling": false, "quantity": 1000, "behaviour": 0 } ],
      "rateTables": [ { "currencyCode": "USD", "tiers": [ { "upToQuantity": null, "unitAmountMinor": 1 } ] } ]
    }
  ],
  "entitlements": [ { "key": "ai.chat", "limitKind": 0 } ]
}
```

- `subscriberScope`: `0` Organization, `1` each person. It can't be changed by a later update.
- `countsMembers` may be left out when there is only one quantity item.
- `includedQuantity` and every `subLimits` entry apply **per place**.
- Numeric enums: `aggregation` 0 Sum / 1 Max / 2 LastValue; `resetPolicy` 0 Periodic / 1 Never /
  2 CarryForward; `window` 0 Hour / 1 Day / 2 Week; `behaviour` 0 Refuse / 1 Throttle (the builder calls it "then reported"); intervals
  0 Day / 1 Week / 2 Month / 3 Year.

#### `POST /api/subscription-plans/prices` — price it per place

Permission `subscription-plan::manage`. A price is created after the plan. Set `quantityItemKey` to
the counting item so that buying N means N places.

```json
{ "planId": "<plan id from the create response>", "currencyCode": "USD", "unitAmountMinor": 1000,
  "interval": 2, "intervalCount": 1, "quantityItemKey": "seat" }
```

Leave `quantityItemKey` empty for a flat price. The number of places is then the counting item's
`maxQuantity`, which must be set.

### 7.2 Subscribing

#### `POST /api/subscriptions/preview` — quote before subscribing

Permission `subscription::read`. Same body as subscribe. For a per-person plan, an existing
organization subscription is **not** reported as a blocker.

#### `POST /api/subscriptions` — subscribe to a per-person plan

Permission `subscription::manage`. The counting quantity is the number of places.

```json
{
  "planCode": "ai-seat",
  "priceId": "43e4c270-7d68-481f-8f22-a221ddf62249",
  "quantities": [ { "itemKey": "seat", "quantity": 5 } ],
  "timeZoneId": "Asia/Dhaka",
  "discountCode": null
}
```

The response is the subscription (`status: "Incomplete"` until paid) with a `checkoutUrl` when payment
is due. Send the buyer there. The subscription becomes `Active` when the provider's webhook lands,
which may be a few seconds after the browser returns.

### 7.3 Finding per-person subscriptions

#### `GET /api/subscriptions/member-based` — the organization's per-person subscriptions

Permission `subscription::read`. Returns an array of subscriptions (live or awaiting payment), the
same shape as `current`. **`GET /api/subscriptions/current` never returns these**; it answers for
the organization's own subscription only. An empty array is a normal answer.

```json
{ "success": true, "data": [
  { "subscriptionId": "1488665d-…", "status": "Active", "planCode": "ai-seat", "planName": "AI assistant",
    "quantities": [ { "itemKey": "seat", "unitLabel": "seat", "quantity": 5, "minQuantity": 1, "maxQuantity": null, "defaultQuantity": 1 } ],
    "currentPeriodStartUtc": "2026-09-29T17:24:00Z", "currentPeriodEndUtc": "2026-10-29T17:24:00Z",
    "cancelAtPeriodEnd": false, "pendingQuantityChange": null, "pendingPlanChange": null,
    "recurringAmountMinor": 5405, "checkoutUrl": null, "version": 3 }
] }
```

#### `GET /api/subscriptions/mine` — the places the signed-in person holds

Permission **`entitlement::read`** (not `subscription::read`), so an ordinary user can call it.

```json
{ "success": true, "data": [
  { "subscriptionId": "1488665d-…", "planCode": "ai-seat", "planName": "AI assistant",
    "status": "Active", "seatNumber": 2, "currentPeriodEndUtc": "2026-10-29T17:24:00Z", "cancelAtPeriodEnd": false }
] }
```

### 7.4 Places and people

#### `GET /api/subscriptions/{subscriptionId}/members` — who holds which place, and each place's usage

Permission `subscription::read`. Works on a lapsed subscription too.

```json
{ "success": true, "data": {
  "subscriptionId": "1488665d-…",
  "purchased": 5, "held": 2, "available": 3,
  "scheduledPlaces": null, "scheduledAtUtc": null,
  "seats": [
    { "subscriptionId": "1488665d-…", "userId": "e7c85911-…", "seatNumber": 1, "assignedAtUtc": "2026-09-29T18:00:00Z", "releasedAtUtc": null }
  ],
  "usage": [
    { "seatNumber": 1, "userId": "e7c85911-…", "meterKey": "token", "unitLabel": "token", "quantityScale": 0,
      "included": 100000, "used": 130, "remaining": 99870, "overage": 0,
      "periodEndUtc": "2026-10-29T17:24:00Z", "updatedAtUtc": "2026-09-29T18:03:08Z",
      "subLimits": [ { "window": "Hour", "windowCount": 1, "rolling": false, "behaviour": "Refuse",
                       "quantity": 1000, "used": 130, "remaining": 870, "exceeded": false,
                       "windowStartUtc": "2026-09-29T18:00:00Z", "windowEndUtc": "2026-09-29T19:00:00Z" } ] }
  ]
} }
```

- `available` is a snapshot, not a promise. Only the assignment itself settles a race for the last
  place.
- `usage` has one entry per place and meter in its current window. A place that was used and then
  released is still listed, with `userId: ""`.

#### `POST /api/subscriptions/{subscriptionId}/members` — assign people

Permission `subscription::manage`. People are named in the body (you're usually filling places for
others); the token only decides which organization's subscription may be touched.

```json
{ "userIds": [ "user-a", "user-b", "user-c" ] }
```

The call succeeds whenever the subscription can take members at all, and answers **per person**:

```json
{ "success": true, "data": {
  "subscriptionId": "1488665d-…",
  "assigned": [ { "subscriptionId": "1488665d-…", "userId": "user-a", "seatNumber": 2, "assignedAtUtc": "2026-09-29T18:05:00Z", "releasedAtUtc": null } ],
  "refused": [
    { "userId": "user-b", "reasonCode": "subscription_member_meter_overlap",
      "reason": "This person already holds a place on user-test-4, which meters the same usage. A use could not tell which place to draw on." },
    { "userId": "user-c", "reasonCode": "subscription_member_limit_reached",
      "reason": "This subscription already has all the people it was bought for." }
  ]
} }
```

Whole-call failures (nothing assigned): `subscription_member_required`,
`subscription_member_count_ambiguous`, `subscription_not_found`, `subscription_not_member_based`,
`subscription_not_live`.

#### `DELETE /api/subscriptions/{subscriptionId}/members/{userId}` — release a person

Permission `subscription::manage`. Works on a lapsed subscription.

```json
{ "success": true, "data": {
  "subscriptionId": "1488665d-…", "userId": "user-a",
  "seatNumber": 2, "assignedAtUtc": "2026-09-29T18:05:00Z", "releasedAtUtc": "2026-09-30T09:00:00Z"
} }
```

`404 subscription_member_not_assigned` when they don't hold a place. Before #619 the response carried
`seatNumber: null` and an empty `assignedAtUtc`.

### 7.5 Changing places, plan and cadence

These are the ordinary subscription endpoints. The per-person differences are below.

| Endpoint | Permission | Per-person behaviour |
| --- | --- | --- |
| `POST /api/subscriptions/{id}/quantities/preview` | `subscription::read` | Quote only. |
| `PUT /api/subscriptions/{id}/quantities` | `subscription::manage` | More places: charged now, prorated. Fewer: scheduled for the period end; refused with `subscription_member_seats_occupied` if people would be stranded. |
| `DELETE /api/subscriptions/{id}/quantities/pending` | `subscription::manage` | Withdraws a scheduled decrease; the places open again. |
| `POST /api/subscriptions/{id}/plan/preview` | `subscription::read` | Quote only. |
| `PUT /api/subscriptions/{id}/plan` | `subscription::manage` | Only to another per-person plan (else `subscription_plan_change_scope_mismatch`). Send the **current number of places** in `quantities`, or you change places too. |
| `DELETE /api/subscriptions/{id}/plan/pending` | `subscription::manage` | Withdraws a scheduled plan or cadence change. |

```json
PUT /api/subscriptions/{id}/quantities
{ "version": 3, "quantities": [ { "itemKey": "seat", "quantity": 7 } ] }

PUT /api/subscriptions/{id}/plan
{ "planCode": "ai-seat-pro", "priceId": "1f13404b-…", "quantities": [ { "itemKey": "seat", "quantity": 7 } ] }
```

### 7.6 Cancelling

| Endpoint | Permission | Per-person behaviour |
| --- | --- | --- |
| `DELETE /api/subscriptions/{id}` | `subscription::manage` | Default: at period end. Places keep working until then, then everyone is released. `?immediately=true`: ends now and releases everyone at once. |
| `DELETE /api/subscriptions/{id}/cancellation` | `subscription::manage` | Withdraws a scheduled cancel; places stay as they are. |

### 7.7 Using and reading usage

#### `POST /api/subscription-usage` — record usage (the only real gate)

Permission `subscription-usage::manage`. Must be called **with the person's own token**: their place is
found from who is calling, and there's no field to name someone else.

```json
{ "meterKey": "token", "quantity": 250, "idempotencyKey": "chat-9f2c…", "enforce": true, "metadata": { "feature": "chat" } }
```

```json
{ "success": true, "data": {
  "allowed": true, "userId": "", "meterKey": "token", "unitLabel": "token",
  "periodKey": "M20260929T172400Z", "periodStartUtc": "2026-09-29T17:24:00Z", "periodEndUtc": "2026-10-29T17:24:00Z",
  "quantityScale": 0, "included": 100000, "used": 380, "remaining": 99620, "overage": 0,
  "replayed": false, "subLimitExceeded": false, "exceededSubLimits": [], "projection": 0
} }
```

- `used`/`remaining` are **the caller's place's**, including this call.
- `allowed: false` means refused and nothing was counted. Check `exceededSubLimits` to tell a pace
  refusal from an exhausted allowance.
- Repeating an `idempotencyKey` returns the first answer with `replayed: true`.

#### `GET /api/subscription-usage/mine` — what the signed-in person may spend

Permission `subscription-usage::read`. One item per meter, chosen exactly as a recording would: the
person's place first, then the organization's plan. Same item shape as above. Returns
`404 subscription_not_found` when the person has no place and the organization has no subscription.

#### `GET /api/subscription-usage/current` — the organization's own subscription

Permission `subscription-usage::read`. Unchanged. It answers for the **organization's** subscription,
not per-person ones. For per-place figures use `…/members` (administrator) or `…/usage/mine` (the
person).

### 7.8 Entitlements

#### `GET /api/entitlements` and `GET /api/entitlements/{key}`

Permission `entitlement::read`. The caller's places are consulted **before** the organization's plan;
where both declare the same key, the place's plan answers. Answering `allowed` for a per-person
feature works. Metered `used`/`remaining` are the caller's **own place's** figures (once the entitlement fix lands; see [known gaps](#12-known-gaps)).

### 7.9 Invoices

Per-person subscriptions appear in `GET /api/subscriptions/invoices` like any other, as the
subscription fee and, separately, the period's overage. Permission `subscription::read-invoice`.

---

## 8. Which API to call when

| I want to… | Call | Who |
| --- | --- | --- |
| Show which plans are per person | `GET /api/subscription-plans` → `subscriberScope === 1` | Admin UI |
| Price a per-person signup | `POST /api/subscriptions/preview` | Admin UI |
| Buy places | `POST /api/subscriptions`, then send the buyer to `checkoutUrl` | Admin UI |
| Find the organization's per-person subscriptions | `GET /api/subscriptions/member-based` | Admin UI |
| See who is on it and how much each place used | `GET /api/subscriptions/{id}/members` | Admin UI |
| Give people places | `POST /api/subscriptions/{id}/members` | Admin UI |
| Take someone off | `DELETE /api/subscriptions/{id}/members/{userId}` | Admin UI |
| Buy more places / give some up | `POST …/quantities/preview`, then `PUT …/quantities` | Admin UI |
| Undo a scheduled decrease | `DELETE …/quantities/pending` | Admin UI |
| Upgrade, or switch monthly ↔ yearly | `POST …/plan/preview`, then `PUT …/plan` | Admin UI |
| Undo a scheduled plan change | `DELETE …/plan/pending` | Admin UI |
| Cancel / undo a cancel | `DELETE /api/subscriptions/{id}` / `DELETE …/cancellation` | Admin UI |
| Know whether *I* have a place | `GET /api/subscriptions/mine` | End-user app |
| Show *my* remaining allowance | `GET /api/subscription-usage/mine` | End-user app |
| Hide or show a feature | `GET /api/entitlements/{key}` | End-user app |
| Spend allowance (and stop at the limit) | `POST /api/subscription-usage` with `enforce: true` | End-user app / its backend, **with the user's token** |

---

## 9. How a client consumes it

### 9.1 Administrator console

```text
1. GET  /api/subscription-plans                     filter subscriberScope === 1
2. POST /api/subscriptions/preview                  show the quote
3. POST /api/subscriptions                          redirect to data.checkoutUrl
4. (buyer pays; webhook activates)                  poll member-based until status is Active
5. GET  /api/subscriptions/member-based             one card per subscription
6. GET  /api/subscriptions/{id}/members             places, holders, per-place usage
7. POST /api/subscriptions/{id}/members             show data.assigned and data.refused separately
8. DELETE /api/subscriptions/{id}/members/{userId}  then re-read step 6
```

- **Show refusals per person.** A call that assigns 3 and refuses 7 is a success; hiding the 7 sends
  the administrator looking for problems that aren't there.
- **Re-read after every change**, and when the page is refreshed. A payment can land with no action in
  the client.
- **Draw a place above `scheduledPlaces` as "removed on <date>"**, not as empty; assigning to it is
  refused.
- **Carry the current number of places into a plan change.**

### 9.2 End-user application

```text
On sign-in / app load:
  GET /api/subscriptions/mine          → which plans I have a place on (empty = none)
  GET /api/subscription-usage/mine     → my balances, one per meter
  GET /api/entitlements                → which features to show

On each metered action (ideally from the app's backend, forwarding the user's token):
  POST /api/subscription-usage { meterKey, quantity, idempotencyKey, enforce: true }
    allowed: true   → do the work; show used/remaining from the response
    allowed: false  → don't; exceededSubLimits says whether it's the pace ("try again in an hour")
                      or the allowance ("you've used this month's tokens")
    subLimitExceeded: true (Throttle pace) → allowed, but warn or slow down
```

- **Record with the person's token.** A service token has no user, so it holds no place and draws on
  the organization's plan instead.
- **Use a stable `idempotencyKey`** per real-world action (e.g. the message id), so a retry isn't
  billed twice.
- **Treat an empty `mine` as "no place"**, not as an error.
- **Don't gate on a prior read.** Two requests can both see "1 left"; only `enforce` on the recording
  decides.

---

## 10. Permissions

Permissions use the resource form `blocks-utilities::<area>::<action>`.

| Permission | Endpoints (per-person relevant) | Give to |
| --- | --- | --- |
| `subscription-plan::read` | `GET /subscription-plans`, `GET /subscription-plans/{id}` | Admins, anyone choosing a plan |
| `subscription-plan::manage` | create / update / archive plans and prices | Plan authors |
| `subscription::read` | `GET /subscriptions/current`, **`GET /subscriptions/member-based`**, **`GET /subscriptions/{id}/members`**, every `…/preview`, `GET /subscriptions/{id}/audit` | Billing admins |
| `subscription::manage` | subscribe, cancel, withdraw cancellation, change plan / quantity and their pending withdrawals, payment-method setup, **`POST /subscriptions/{id}/members`**, **`DELETE /subscriptions/{id}/members/{userId}`** | Billing admins who fill places |
| `subscription::read-invoice` | `GET /subscriptions/invoices`, `GET /subscriptions/invoices/{id}/pdf` | Finance, billing admins |
| `subscription::manage-invoice` | `POST /subscriptions/invoices/{id}/resend` | Finance |
| `entitlement::read` | `GET /entitlements`, `GET /entitlements/{key}`, **`GET /subscriptions/mine`** | **Every signed-in user** |
| `subscription-usage::read` | `GET /subscription-usage/current`, **`GET /subscription-usage/mine`**, `POST /subscription-usage/overage/preview` | Every signed-in user who sees their balance |
| `subscription-usage::manage` | `POST /subscription-usage` | Every user whose actions are metered (or the backend acting with their token) |

**Minimum bundles**

- **End user:** `entitlement::read`, `subscription-usage::read`, `subscription-usage::manage`.
- **Seat administrator:** `subscription::read`, `subscription::manage`, `subscription-plan::read`.
- **Plan author:** `subscription-plan::read`, `subscription-plan::manage`.

A resource with no permission row answers `403` for every endpoint that names it.

---

## 11. Error codes

| Code | HTTP | Where | Meaning / what to do |
| --- | --- | --- | --- |
| `subscription_plan_member_quantity_ambiguous` | 400 | create/update plan | A per-person plan selling several quantities must mark exactly one as counting people. |
| `subscription_member_required` | 400 | assign | `userIds` was empty. |
| `subscription_member_count_ambiguous` | 400 | assign | The plan doesn't say how many places it has; fix the plan (mark the counting item; give a flat-priced one a maximum). |
| `subscription_member_already_assigned` | per person | assign | Already on this subscription. Harmless to ignore when re-sending a list. |
| `subscription_member_meter_overlap` | per person | assign | Holds a place on another live per-person plan metering the same key. Release them there first. |
| `subscription_member_limit_reached` | per person | assign | Every fillable place is taken; the message says if a scheduled decrease is the reason. Buy more places. |
| `subscription_member_not_assigned` | 404 | release | Not on this subscription. |
| `subscription_member_seats_occupied` | 409 | decrease places | More people than the new number; release some first. |
| `subscription_not_member_based` | 400 | members endpoints | That's the organization's own subscription. |
| `subscription_not_live` | 409 | assign | Not active (unpaid, or ended). |
| `subscription_not_found` | 404 | any | No such subscription for this organization, or (`usage/mine`) nothing to draw on. |
| `subscription_plan_change_scope_mismatch` | 400 | change plan | Can't move between an organization plan and a per-person plan. |

---

## 12. Known gaps

| Gap | Effect | Workaround |
| --- | --- | --- |
| **Before the entitlement fix (branch `fix/entitlement-place-balance`)**: metered figures on `GET /api/entitlements` for a per-person meter read the subscription-wide counter, but per-person usage is counted per place. | `used`/`remaining` looked untouched (`used: 0`), though `allowed` was right. | Fixed: they now show the caller's own place. Gate with `enforce: true` on the recording either way. |
| **No "record on behalf of"**: usage is always recorded against the caller's own place. | A backend can't spend a named user's place with a service token. | Forward the user's token from the backend. |
| **Discounts don't reduce overage.** The discount applies to the subscription fee only. | A 20 %-off code leaves overage at full rate. | By design so far; confirm with product if that's wanted. |
| **Before #619**: an immediate cancel released everyone but left the usage rows naming the last holder, and the release response had no place number. | Cosmetic, on ended subscriptions only. | Fixed by #619. |
