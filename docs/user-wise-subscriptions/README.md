# User-wise subscriptions

How a subscription is sold to an organization *per person* rather than as one shared thing, and how
that coexists with everything already sold to organizations.

Tracks [issue #535](https://github.com/SELISEdigitalplatforms/blocks-utilities/issues/535).
Implemented on `feat/subscription-seat-endpoints`, open as
[PR #584](https://github.com/SELISEdigitalplatforms/blocks-utilities/pull/584) against
`dev-user-subscription`.

---

## The problem

An organization buys a subscription and everybody in it shares whatever the plan includes. That is
the right shape for a plan selling capacity to the organization — so many widgets, so many channels
— and the wrong shape for a plan selling something to a person. Ten million AI tokens a month
shared across an organization is spent by whoever gets there first; the same plan sold per person is
ten million *each*.

Both shapes have to exist at once, in the same organization. A typical customer wants the org plan
for its shared capacity **and** user plans for individual AI usage, running side by side.

---

## Scope, as decided

These were open questions during design. The answers below are what is built.

| Question | Decision |
| --- | --- |
| When are places assigned? | At purchase, after purchase, or one at a time — all three. Plus a bulk assign in one call. |
| Can a place be reassigned? | Yes. Usage stays with the place and rides to the period boundary; it is not reset when the place changes hands. |
| What does an unassigned place grant? | Nothing. |
| Can one person hold two user-wise plans in an organization? | Yes. Uniqueness is per subscription, not per organization. |
| Is the allowance per place or shared? | **Per place.** $200 for 5 places at 10M each is 50M total. |
| Does the allowance belong to the place or the person? | **The place.** Cycling people through one paid place mints no allowance. |
| Can a meter be capped faster than the billing period? | Yes — hourly, daily or weekly sub-caps. |
| Which free place does a newcomer get? | The one with the most allowance remaining. |
| What if places are reduced below the number occupied? | Refused until enough are released. Reductions still apply at period end. |
| Does a sub-cap refuse or throttle? | Either — configured per meter. |
| Can places of different kinds be mixed? | Yes, via two subscriptions or two line items. |
| What must not change? | Plan, price, meter and quantity authoring, and proration, quantity change and plan change, all work exactly as they do today. The new work adds options; it removes none. |

### Naming

Internally the unit is a `Seat` / `SeatNumber`. Externally it is a **member**: the endpoints are
`/members`, the request is `AssignMemberRequest`, the outcomes are `MemberAssignmentOutcome` and
`MemberReleaseOutcome`. This is deliberate — production already has a quantity item literally named
`seat` meaning something unrelated (a purchased ceiling on a flat-priced plan), and reusing the word
in the API would have collided with it in customers' own data.

---

## Domain model

### `SubscriptionAssignment`

One document per person occupying one place on one subscription.

```
TenantId, OrganizationId, SubscriptionId, UserId, SeatNumber (int, 1-based),
AssignedAtUtc, ReleasedAtUtc?, AssignedByUserId, CorrelationId
```

A release sets `ReleasedAtUtc` rather than deleting, so the history of who held what survives.

Two partial unique indexes enforce the invariants, both filtered on `ReleasedAtUtc` being
`$type: "null"` so that only *active* rows are constrained:

- `ux_assignment_subscription_member_active` — (tenant, subscription, user). One person cannot hold
  two places on the same subscription.
- `ux_assignment_subscription_seat_active` — (tenant, subscription, seat). One place cannot hold two
  people.

> **Why a partial index and not a query predicate.** A Mongo query's `$ne` matches documents where
> the field is *missing*, but a partial index's filter expression cannot use `$ne` at all. `$type:
> "null"` matches an explicit null and a missing field, which is what both released-state
> representations need. This asymmetry between what a query can say and what an index filter can say
> comes up repeatedly in this codebase.

### Marking which quantity counts people

`PlanQuantityItem.CountsMembers` (and its snapshot, `SubscriptionQuantityItem.CountsMembers`) marks
which of a plan's quantity items is the one counting people. A plan with exactly one quantity item
needs no mark; a plan with several must mark exactly one, or assignment is refused with
`subscription_member_count_ambiguous`.

How many places a subscription actually has is **pricing-aware**:

```csharp
var pricedPerMember = !string.IsNullOrWhiteSpace(subscription.Price.QuantityItemKey) &&
    string.Equals(subscription.Price.QuantityItemKey, counting.ItemKey, StringComparison.Ordinal);

return pricedPerMember ? counting.Quantity : counting.MaxQuantity;
```

- **Per-member price** — the organization is charged per place, so the quantity it *bought* is how
  many it has.
- **Flat price** — the organization pays the same whether it uses one place or ten, so the *ceiling*
  (`MaxQuantity`) is how many it has. This is exactly the shape already in production: `seat`,
  quantity 1, max 10, 115 CHF flat.

A flat-priced plan with no `MaxQuantity` has no ceiling to derive, and assignment is refused rather
than guessed at.

---

## Identity: how usage is keyed

This is the core of the design and the part most worth understanding before changing anything.

A counter id is built by composition, never independently:

```
sub-1:ai_tokens:M20260901T000000Z        // the subscription's own
sub-1:ai_tokens:M20260901T000000Z:s2     // place 2's own
```

Three properties follow, and all three are load-bearing:

1. **A null place composes the three-part identity exactly.** Every counter and projection row
   already written is addressed unchanged, so there is no migration and no organization appears to
   start afresh.
2. **The identity names the place, not the person.** Releasing somebody who has spent their window
   and assigning somebody else returns to the *same* counter. This is what makes "the allowance
   belongs to the place" true rather than merely intended.
3. **A projection row and the counter it projects are addressed identically.**
   `SubscriptionUsageCurrent.CreateId(..., int? seat)` delegates to
   `SubscriptionUsageCounter.CreateId`. See [Bugs found](#bugs-found-during-the-work) — this was
   not true at first, and the failure mode was silent.

### Sub-cap windows

A sub-cap counts against a second counter in a second window, never the same document as the period
balance — one counter for both would make the cap the period's own balance under another name,
enforcing nothing.

`UsageWindowKey` produces keys such as `h20260914T120000Z`, `d20260914T000000Z`,
`w20260914T000000Z`. The window codes are **lowercase** (`h`/`d`/`w`) precisely so a window key can
never collide with a `PeriodKey`, whose codes are uppercase. Weeks start Monday (ISO-8601), and
windows are truncated to the clock rather than offset from each subscriber's anchor — a pace
measured from each subscriber's own signup instant cannot be reasoned about by anybody comparing
two of them.

Meter fields: `SubLimitWindow`, `SubLimitQuantity`, `SubLimitBehaviour` (`Refuse` | `Throttle`).

Throttle cannot actually slow a caller down from inside this module, so it reports
`subLimitExceeded: true` on the response and allows the usage. A cap that reported nothing would
have capped nothing.

A refused use must **not** be left counted in the short window, or the next attempt opens already
spent and somebody who waited exactly as instructed is refused again. `ReversePaceAsync` exists for
this.

---

## Resolution: whose allowance a caller spends

`ISubscriberSubscriptionResolver` answers "what may this caller draw on" in one place, returning
`IReadOnlyList<ResolvedSubscription>` where `ResolvedSubscription` is
`(SubscriptionDetail Subscription, int? SeatNumber)` — **places first, then the organization's own**.

One place, because entitlement asking *"what may this person do"* and usage asking *"whose allowance
does this consume"* must answer identically. If they disagree, somebody is told they may act and the
act is then counted against a plan they are not on.

Selection is **per meter**, not per caller: `SubscriberSubscriptionSelection.ForMeter(resolved, key)`
takes the first resolved subscription that meters that key. So somebody with their own AI allowance
still records against the organization's plan for a meter their own plan says nothing about.

A caller with no user id (background work, machine tokens) is never asked about places at all —
the answer is always empty and asking is a round trip per call.

---

## API surface

### Members

| Route | Purpose |
| --- | --- |
| `POST /subscriptions/{id}/members` | Assign one or many people in a single call. |
| `DELETE /subscriptions/{id}/members/{userId}` | Release one person's place. |
| `GET /subscriptions/{id}/members` | Who currently holds which place. |

`POST` reports **per-person outcomes** rather than failing the batch: `assigned[]` and `refused[]`,
each refusal carrying a reason code. A ten-name batch against a two-place subscription assigns two
and explains the other eight.

Refusal codes: `subscription_member_required`, `subscription_member_count_ambiguous`,
`subscription_member_limit_reached`, `subscription_member_already_assigned`,
`subscription_member_not_assigned`.

The batch reads active assignments **once** and tracks what it assigns in memory, because nothing it
writes is visible to a fresh read until it lands — a per-person re-read would walk a ten-name batch
straight past a two-place subscription.

### Usage

| Route | Answers |
| --- | --- |
| `GET /subscription-usage/current` | The **organization's** subscription, as a whole. Unchanged. |
| `GET /subscription-usage/mine` | What **the caller** is the one spending. New. |

`/mine` is a separate route rather than a flag on `/current`, because the two answer different
questions. `/current` is built around exactly one subscription throughout — it counts how many
meter-windows the plan should have and refuses a projection holding fewer, a judgement with no
meaning spread across two plans.

`/mine` returns **one item per meter**, chosen the way a recording chooses: the caller's place
first, the organization's for any meter no place of theirs covers. So the balance shown is the
balance the next call actually draws down. It reads the counters always — there is no per-place
projection to prefer, and no `readMode` parameter.

---

## Which place a newcomer gets

Free places are ranked by **allowance remaining**, summed across the plan's current meter windows,
ties keeping the lowest number.

Not by "least spent". The two diverge under carry-forward: a place that saved last window opens this
one with more than the plan includes, so the place with the smaller balance can be the poorer of the
two. `MeterAllowanceResolver.EffectiveAsync` is used, which is the same allowance the recording path
resolves.

Ordering is computed **once per batch** and offered as a queue that is peeked and only dequeued once
the write lands — so an `AlreadyHeld` refusal leaves that place available for the next name rather
than burning it.

A subscription nobody has used is entirely ties, so it hands out 1, 2, 3… exactly as before. An
organization-wise plan never reaches the ranking at all: the dependencies are optional constructor
parameters, and with fewer than two free places there is nothing to rank.

---

## Lifecycle

**Quantity increase** — more places, immediately assignable. Priced and prorated exactly as any
other quantity change.

**Quantity decrease below the number occupied** — refused with `subscription_member_seats_occupied`
and a message naming how many people would be stranded. A decrease is not refunded, so it *will*
take effect; filling or keeping places it removes strands somebody the moment the period turns over.
A decrease already scheduled is also respected when assigning: the smaller of bought and pending is
what can be filled.

**Cancellation** — `SubscriptionCancellationEffectiveProcessor` calls `ReleaseAllAsync`, emptying
every place when the paid period actually ends. Not at the moment of cancelling: a subscriber who
cancels keeps what they paid for right up to the boundary.

**Plan change, proration, everything else** — unchanged. Nothing in this work touches the pricing,
rating, invoicing or proration paths.

---

## Migration and deployment

**No data migration.** A null place composes the identity every stored counter and projection row
already uses.

**Index changes**, all handled idempotently in `EnsureIndexesAsync` at startup:

| Index | Action |
| --- | --- |
| `ux_usage_current_subscription_meter_period_user_seat_v3` | Created. Adds `SeatNumber` to the existing unique key. |
| `ux_usage_current_subscription_meter_period_user_v2` | Dropped. Left in place it rejects a second place's row as a duplicate of the first's — both carry no user — so a seated subscription would publish exactly one place and every other place's usage would go unprojected. |
| `ux_usage_current_subscription_meter_period` | Dropped (pre-existing legacy drop). |
| `ux_assignment_subscription_member_active`, `ux_assignment_subscription_seat_active` | Created. |

A brand-new tenant database never created the dropped indexes, so failing to find them is expected,
not an error.

> **Adding a field to an already-unique index is safe** — the key becomes strictly more permissive,
> so nothing that fits today stops fitting. *Replacing* the discriminating field is the dangerous
> shape, and needs backfill-then-swap. This change is the safe one.

### Deployment gate — blocks any release

Before this ships to an environment with live customers:

1. Run `scratchpad/audit.js` against **production**.
2. Confirm **zero** documents missing `Plan.SubscriberScope`, on **every** tenant.
3. Confirm every Api and Worker instance is running a build carrying the backfill.

### Branch direction

`dev-user-subscription` was cut from `dev` *before* the revert of the earlier commits. Merges go
**`dev-user-subscription` → `dev` only, never the reverse.**

### One dev-only caveat

The place-counter id changed spelling *within this branch* (`:2` → `:s2`). Nothing in production
holds one — the feature is unreleased — but a dev tenant tested against an earlier build of this
branch will read as starting fresh.

---

## Backwards compatibility

Every claim below has a test asserting it, because "we didn't break the existing thing" is the
requirement this work is most likely to violate quietly.

- An organization-wise plan resolves the same subscription, addresses the same counter and reads the
  same projection row it always has.
- `GET /subscription-usage/current` is byte-for-byte unchanged in shape and content for an
  organization-wise plan.
- Plans, prices, meters and quantity items are authored exactly as today. Every new field is
  optional and every default is today's behaviour.
- New behaviour is reached **only** by a plan explicitly authored with `SubscriberScope.User`.
- Optional constructor parameters mean the running host gets the full behaviour (the .NET DI
  container does honour default values for unresolvable parameters) while every pre-existing test
  keeps the old behaviour with no edit.

---

## Bugs found during the work

Worth recording, because two of them were invisible to a passing test suite and the shape of that
invisibility is instructive.

**1. The projection held whichever place published last.** Counters became per-place in stage 2, but
`SubscriptionUsageCurrent` rows stayed keyed `{subscription, meter, period}`. Two members of one
subscription wrote the same document. Every test passed because tests exercise the counter and mock
the publisher — the defect lived in a seam no test crossed.

**2. `/current` returned a member's row as the organization's.** `ListCurrentAsync` filtered
`UserId == ""` but nothing on the place, and a place's row carries no user. So an org admin could be
shown one person's usage as the organization's — and the *count* of rows returned is what decides
whether the projection is allowed to answer the read at all. Fixed with
`Eq(SeatNumber, null)`, which matches both an explicit null and a missing field, i.e. exactly the
set "aggregate rows, old and new".

**3. A row and its counter were spelled differently** — `:2` against `:s2`.
`UsageProjectionReconciler` looks a row's counter up *under the row's own id*, so that lookup never
matched for a place's row and drift went unnoticed forever. Invisible because both spellings were
internally consistent; only the join between them was wrong. Fixed by making
`SubscriptionUsageCurrent.CreateId` delegate to the counter's, so the two are unrepresentable
separately rather than merely equal today.

**4. A plan sold by the place advertised an allowance nobody held.** The background seed and refresh
wrote a subscription-wide row for a user-wise plan, naming an included quantity nobody could spend
beside a balance that never moved however much the members used. A sweep knows nothing of who holds
which place, so the only row it can write is the one that is not anybody's; it now writes none.
Reconciliation and entitlements still run, because those describe the subscription itself and are
true of either kind of plan.

---

## Testing

Conventions are the repo's own: xUnit v2, FluentAssertions, Moq, in `server/XUnitTest`, mirrored by
capability folder; readable-sentence test names; `because` reasons stating the business consequence;
class-level `<remarks>` naming the class of customer-visible bug the file guards against.

New files:

```
SubscriptionMemberServiceTests.cs        assignment, release, limits, ambiguity
MemberQuantityAuthoringTests.cs          which quantity counts people, and the pricing rule
SeatOfferOrderTests.cs                   which free place a newcomer gets
PerSeatCounterTests.cs                   counter identity: per place, per period, seatless unchanged
PerSeatCarryForwardTests.cs              a place's leftovers do not open another's window
SubscriberSubscriptionResolverTests.cs   whose allowance a caller's action draws on
UsageSubLimitTests.cs                    pace as distinct from amount; refuse vs throttle
UsageWindowKeyTests.cs                   window truncation, ISO weeks, keyspace separation
UsageReadMineTests.cs                    what a caller is shown of their own allowance
```

**Every behavioural claim in this document was mutation-tested**: the guard was inverted or removed
and a named test confirmed to fail. This caught a genuine coverage gap — an early carry-forward
mutation passed all 4,075 tests, which is what `PerSeatCarryForwardTests.cs` was written for.

Current state: **4,108 unit tests pass**; the 36 usage-current integration tests pass against dev
Mongo via `BLOCKS_IT_MONGO`.

> A clean build reports ~8,630 warnings, nearly all `CA1707` from the underscore test names. That is
> the baseline, not a regression — compare against it rather than assuming a warning is new.

---

## Not done

- **No client work.** `git diff` against `dev-user-subscription` shows zero changes under `client/`.
  The capability is API-only; there is no console UI for assigning members. Whether #535 wants one is
  an open question.
- **The production audit gate** (above) has not been run.
- **Known-flaky tests, unrelated to this work but still open:**
  `SubscriptionWorkDispatcherTests` / `SubscriptionWorkSchedulerTests` share a process-global
  `ActivityListener`; `FinancialDocumentRendererHealthMonitorTests` is flaky on its reprobe
  interval. These deserve their own issue.
